using System.Text.Json;
using System.Text.Json.Serialization;
using UsbFileSync.Core.Models;

namespace UsbFileSync.Core.Services;

public sealed class JsonCloudAccountStore : ICloudAccountStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public JsonCloudAccountStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A cloud account file path is required.", nameof(filePath));
        }

        _filePath = filePath;
    }

    public IReadOnlyList<CloudAccountRegistration> Load()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(_filePath);
            var accounts = JsonSerializer.Deserialize<List<CloudAccountRegistration>>(stream, SerializerOptions);
            return accounts ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<CloudAccountRegistration> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_filePath}.tmp";

        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, accounts, SerializerOptions);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
