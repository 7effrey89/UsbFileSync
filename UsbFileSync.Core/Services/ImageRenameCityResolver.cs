using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Core.Services;

internal sealed class ImageRenameCityResolver : IImageRenameCityResolver
{
    private static readonly string[] CityAddressPropertyNames = ["city", "town", "village", "municipality", "hamlet", "county"];
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly ImageRenameCityLanguagePreference _cityLanguagePreference;
    private readonly ConcurrentDictionary<string, string?> _cityCache = new(StringComparer.OrdinalIgnoreCase);

    public ImageRenameCityResolver(ImageRenameCityLanguagePreference cityLanguagePreference = ImageRenameCityLanguagePreference.EnglishThenLocal)
    {
        _cityLanguagePreference = cityLanguagePreference;
    }

    public string? TryResolveCity(IVolumeSource volume, string relativePath, string extension)
    {
        ArgumentNullException.ThrowIfNull(volume);

        if (!SupportsMetadataLookup(extension))
        {
            return null;
        }

        try
        {
            using var stream = volume.OpenRead(relativePath);
            if (!TryReadGpsCoordinates(stream, relativePath, out var coordinates))
            {
                return null;
            }

            var cacheKey = string.Create(
                CultureInfo.InvariantCulture,
                $"{coordinates.Latitude:F4},{coordinates.Longitude:F4}");

            return _cityCache.GetOrAdd(cacheKey, _ => ReverseGeocode(coordinates, _cityLanguagePreference));
        }
        catch
        {
            return null;
        }
    }

    internal static bool TryReadGpsCoordinates(Stream stream, out (double Latitude, double Longitude) coordinates)
    {
        return TryReadGpsCoordinates(stream, fileName: null, out coordinates);
    }

    internal static bool TryReadGpsCoordinates(Stream stream, string? fileName, out (double Latitude, double Longitude) coordinates)
    {
        coordinates = default;
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            var directories = ImageMetadataReader.ReadMetadata(stream);
            foreach (var gpsDirectory in directories.OfType<GpsDirectory>())
            {
                var geoLocation = gpsDirectory.GetGeoLocation();
                if (geoLocation is null)
                {
                    continue;
                }

                coordinates = (geoLocation.Latitude, geoLocation.Longitude);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UsbFileSync");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    internal static bool SupportsMetadataLookup(string extension)
    {
        var normalizedExtension = ImageRenameDefaults.NormalizeExtension(extension);
        return normalizedExtension.Length > 1;
    }

    private static string? ReverseGeocode((double Latitude, double Longitude) coordinates, ImageRenameCityLanguagePreference cityLanguagePreference)
    {
        try
        {
            foreach (var preferredLanguage in GetPreferredReverseGeocodeLanguages(cityLanguagePreference))
            {
                using var request = CreateReverseGeocodeRequest(coordinates, preferredLanguage);
                using var response = HttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                using var stream = response.Content.ReadAsStream();
                using var document = JsonDocument.Parse(stream);
                var city = TryExtractCityName(document.RootElement);
                if (!string.IsNullOrWhiteSpace(city))
                {
                    return city;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    internal static IReadOnlyList<string?> GetPreferredReverseGeocodeLanguages(ImageRenameCityLanguagePreference cityLanguagePreference) =>
        cityLanguagePreference == ImageRenameCityLanguagePreference.LocalThenEnglish
            ? [null, "en"]
            : ["en", null];

    internal static HttpRequestMessage CreateReverseGeocodeRequest((double Latitude, double Longitude) coordinates, string? preferredLanguage)
    {
        var requestUri = string.Create(
            CultureInfo.InvariantCulture,
            $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=10&addressdetails=1&lat={coordinates.Latitude:F6}&lon={coordinates.Longitude:F6}");

        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        if (!string.IsNullOrWhiteSpace(preferredLanguage))
        {
            request.Headers.AcceptLanguage.ParseAdd(preferredLanguage);
        }

        return request;
    }

    internal static string? TryExtractCityName(JsonElement rootElement)
    {
        if (!rootElement.TryGetProperty("address", out var addressElement))
        {
            return null;
        }

        foreach (var propertyName in CityAddressPropertyNames)
        {
            if (addressElement.TryGetProperty(propertyName, out var valueElement) &&
                valueElement.ValueKind == JsonValueKind.String)
            {
                var value = valueElement.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }
}
