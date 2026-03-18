using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Core.Services;

internal sealed class ImageRenameCityResolver : IImageRenameCityResolver
{
    private const int MaxMetadataPrefixLength = 256 * 1024;
    private static readonly string[] CityAddressPropertyNames = ["city", "town", "village", "municipality", "hamlet", "county"];
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly ConcurrentDictionary<string, string?> _cityCache = new(StringComparer.OrdinalIgnoreCase);

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
            if (!TryReadGpsCoordinates(stream, out var coordinates))
            {
                return null;
            }

            var cacheKey = string.Create(
                CultureInfo.InvariantCulture,
                $"{coordinates.Latitude:F4},{coordinates.Longitude:F4}");

            return _cityCache.GetOrAdd(cacheKey, _ => ReverseGeocode(coordinates));
        }
        catch
        {
            return null;
        }
    }

    internal static bool TryReadGpsCoordinates(Stream stream, out (double Latitude, double Longitude) coordinates)
    {
        coordinates = default;
        ArgumentNullException.ThrowIfNull(stream);

        var metadataBytes = ReadMetadataPrefix(stream);
        if (metadataBytes.Length < 8)
        {
            return false;
        }

        if (metadataBytes[0] == 0xFF && metadataBytes[1] == 0xD8)
        {
            return TryReadGpsCoordinatesFromJpeg(metadataBytes, out coordinates);
        }

        return LooksLikeTiff(metadataBytes) &&
               TryReadGpsCoordinatesFromExif(metadataBytes, out coordinates);
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

    private static bool SupportsMetadataLookup(string extension)
    {
        var normalizedExtension = extension.Trim().ToLowerInvariant();
        return normalizedExtension is ".jpg" or ".jpeg" or ".tif" or ".tiff";
    }

    private static byte[] ReadMetadataPrefix(Stream stream)
    {
        var buffer = new byte[8192];
        using var memoryStream = new MemoryStream();
        var remaining = MaxMetadataPrefixLength;
        while (remaining > 0)
        {
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read <= 0)
            {
                break;
            }

            memoryStream.Write(buffer, 0, read);
            remaining -= read;
        }

        return memoryStream.ToArray();
    }

    private static bool TryReadGpsCoordinatesFromJpeg(ReadOnlySpan<byte> data, out (double Latitude, double Longitude) coordinates)
    {
        coordinates = default;
        var index = 2;
        while (index + 4 <= data.Length)
        {
            if (data[index] != 0xFF)
            {
                index++;
                continue;
            }

            while (index < data.Length && data[index] == 0xFF)
            {
                index++;
            }

            if (index >= data.Length)
            {
                return false;
            }

            var marker = data[index++];
            if (marker is 0xD9 or 0xDA)
            {
                return false;
            }

            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                continue;
            }

            if (index + 2 > data.Length)
            {
                return false;
            }

            var segmentLength = ReadUInt16BigEndian(data, index);
            index += 2;
            if (segmentLength < 2 || index + segmentLength - 2 > data.Length)
            {
                return false;
            }

            var segmentData = data.Slice(index, segmentLength - 2);
            if (marker == 0xE1 &&
                segmentData.Length > 6 &&
                segmentData[0] == (byte)'E' &&
                segmentData[1] == (byte)'x' &&
                segmentData[2] == (byte)'i' &&
                segmentData[3] == (byte)'f' &&
                segmentData[4] == 0 &&
                segmentData[5] == 0)
            {
                return TryReadGpsCoordinatesFromExif(segmentData[6..], out coordinates);
            }

            index += segmentLength - 2;
        }

        return false;
    }

    private static bool TryReadGpsCoordinatesFromExif(ReadOnlySpan<byte> exifData, out (double Latitude, double Longitude) coordinates)
    {
        coordinates = default;
        if (!TryReadTiffHeader(exifData, out var littleEndian, out var firstIfdOffset))
        {
            return false;
        }

        if (!TryFindGpsIfdOffset(exifData, firstIfdOffset, littleEndian, out var gpsIfdOffset))
        {
            return false;
        }

        if (!TryReadGpsCoordinate(exifData, gpsIfdOffset, littleEndian, refTag: 0x0001, valueTag: 0x0002, out var latitude))
        {
            return false;
        }

        if (!TryReadGpsCoordinate(exifData, gpsIfdOffset, littleEndian, refTag: 0x0003, valueTag: 0x0004, out var longitude))
        {
            return false;
        }

        coordinates = (latitude, longitude);
        return true;
    }

    private static bool TryReadTiffHeader(ReadOnlySpan<byte> data, out bool littleEndian, out uint firstIfdOffset)
    {
        littleEndian = true;
        firstIfdOffset = 0;
        if (data.Length < 8)
        {
            return false;
        }

        if (data[0] == (byte)'I' && data[1] == (byte)'I')
        {
            littleEndian = true;
        }
        else if (data[0] == (byte)'M' && data[1] == (byte)'M')
        {
            littleEndian = false;
        }
        else
        {
            return false;
        }

        if (ReadUInt16(data, 2, littleEndian) != 42)
        {
            return false;
        }

        firstIfdOffset = ReadUInt32(data, 4, littleEndian);
        return firstIfdOffset < data.Length;
    }

    private static bool TryFindGpsIfdOffset(ReadOnlySpan<byte> data, uint ifdOffset, bool littleEndian, out uint gpsIfdOffset)
    {
        gpsIfdOffset = 0;
        if (!TryReadIfdEntryCount(data, ifdOffset, littleEndian, out var entryCount))
        {
            return false;
        }

        for (var index = 0; index < entryCount; index++)
        {
            var entryOffset = checked((int)ifdOffset + 2 + (index * 12));
            if (entryOffset + 12 > data.Length)
            {
                return false;
            }

            var tag = ReadUInt16(data, entryOffset, littleEndian);
            if (tag != 0x8825)
            {
                continue;
            }

            gpsIfdOffset = ReadUInt32(data, entryOffset + 8, littleEndian);
            return gpsIfdOffset < data.Length;
        }

        return false;
    }

    private static bool TryReadGpsCoordinate(
        ReadOnlySpan<byte> data,
        uint gpsIfdOffset,
        bool littleEndian,
        ushort refTag,
        ushort valueTag,
        out double value)
    {
        value = 0;
        if (!TryReadIfdEntryCount(data, gpsIfdOffset, littleEndian, out var entryCount))
        {
            return false;
        }

        char? hemisphere = null;
        double[]? dms = null;
        for (var index = 0; index < entryCount; index++)
        {
            var entryOffset = checked((int)gpsIfdOffset + 2 + (index * 12));
            if (entryOffset + 12 > data.Length)
            {
                return false;
            }

            var tag = ReadUInt16(data, entryOffset, littleEndian);
            if (tag == refTag)
            {
                hemisphere = TryReadAsciiValue(data, entryOffset, littleEndian) is { Length: > 0 } text
                    ? char.ToUpperInvariant(text[0])
                    : null;
            }
            else if (tag == valueTag)
            {
                dms = TryReadRationalArray(data, entryOffset, littleEndian, expectedCount: 3);
            }
        }

        if (hemisphere is null || dms is null)
        {
            return false;
        }

        value = dms[0] + (dms[1] / 60d) + (dms[2] / 3600d);
        if (hemisphere is 'S' or 'W')
        {
            value = -value;
        }

        return true;
    }

    private static bool TryReadIfdEntryCount(ReadOnlySpan<byte> data, uint ifdOffset, bool littleEndian, out ushort entryCount)
    {
        entryCount = 0;
        if (ifdOffset + 2 > data.Length)
        {
            return false;
        }

        entryCount = ReadUInt16(data, (int)ifdOffset, littleEndian);
        return true;
    }

    private static string? TryReadAsciiValue(ReadOnlySpan<byte> data, int entryOffset, bool littleEndian)
    {
        var count = ReadUInt32(data, entryOffset + 4, littleEndian);
        if (count == 0)
        {
            return null;
        }

        ReadOnlySpan<byte> textBytes;
        if (count <= 4)
        {
            textBytes = data.Slice(entryOffset + 8, (int)count);
        }
        else
        {
            var offset = ReadUInt32(data, entryOffset + 8, littleEndian);
            if (offset + count > data.Length)
            {
                return null;
            }

            textBytes = data.Slice((int)offset, (int)count);
        }

        var terminatorIndex = textBytes.IndexOf((byte)0);
        if (terminatorIndex >= 0)
        {
            textBytes = textBytes[..terminatorIndex];
        }

        return textBytes.Length == 0 ? null : System.Text.Encoding.ASCII.GetString(textBytes);
    }

    private static double[]? TryReadRationalArray(ReadOnlySpan<byte> data, int entryOffset, bool littleEndian, int expectedCount)
    {
        var count = ReadUInt32(data, entryOffset + 4, littleEndian);
        if (count != expectedCount)
        {
            return null;
        }

        var offset = ReadUInt32(data, entryOffset + 8, littleEndian);
        var byteCount = checked((int)count * 8);
        if (offset + byteCount > data.Length)
        {
            return null;
        }

        var values = new double[count];
        for (var index = 0; index < count; index++)
        {
            var rationalOffset = checked((int)offset + (index * 8));
            var numerator = ReadUInt32(data, rationalOffset, littleEndian);
            var denominator = ReadUInt32(data, rationalOffset + 4, littleEndian);
            if (denominator == 0)
            {
                return null;
            }

            values[index] = numerator / (double)denominator;
        }

        return values;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, bool littleEndian) =>
        littleEndian
            ? (ushort)(data[offset] | (data[offset + 1] << 8))
            : (ushort)((data[offset] << 8) | data[offset + 1]);

    private static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, bool littleEndian) =>
        littleEndian
            ? (uint)(data[offset] |
                     (data[offset + 1] << 8) |
                     (data[offset + 2] << 16) |
                     (data[offset + 3] << 24))
            : (uint)((data[offset] << 24) |
                     (data[offset + 1] << 16) |
                     (data[offset + 2] << 8) |
                     data[offset + 3]);

    private static bool LooksLikeTiff(ReadOnlySpan<byte> data) =>
        data.Length >= 4 &&
        ((data[0] == (byte)'I' && data[1] == (byte)'I') ||
         (data[0] == (byte)'M' && data[1] == (byte)'M'));

    private static string? ReverseGeocode((double Latitude, double Longitude) coordinates)
    {
        try
        {
            var requestUri = string.Create(
                CultureInfo.InvariantCulture,
                $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=10&addressdetails=1&lat={coordinates.Latitude:F6}&lon={coordinates.Longitude:F6}");

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            using var response = HttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("address", out var addressElement))
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
        }
        catch
        {
        }

        return null;
    }
}
