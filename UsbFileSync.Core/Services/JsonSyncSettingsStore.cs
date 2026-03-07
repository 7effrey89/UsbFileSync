using System.Text.Json;
using UsbFileSync.Core.Models;

namespace UsbFileSync.Core.Services;

public sealed class JsonSyncSettingsStore : ISyncSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;

    public JsonSyncSettingsStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A settings file path is required.", nameof(filePath));
        }

        _filePath = filePath;
    }

    public SyncConfiguration? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize<SyncConfiguration>(stream, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(SyncConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

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
                JsonSerializer.Serialize(stream, configuration, SerializerOptions);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
