using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UsbFileSync.Platform.Windows;

internal sealed class DropboxTokenStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _rootDirectory;

    public DropboxTokenStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsbFileSync", "DropboxTokens")
            : rootDirectory;
    }

    public DropboxAuthToken? Load(string cacheKey)
    {
        var filePath = GetTokenFilePath(cacheKey);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<DropboxAuthToken>(stream, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Save(string cacheKey, DropboxAuthToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(token);

        Directory.CreateDirectory(_rootDirectory);
        var filePath = GetTokenFilePath(cacheKey);
        var temporaryFilePath = filePath + ".tmp";
        using (var stream = File.Create(temporaryFilePath))
        {
            JsonSerializer.Serialize(stream, token, SerializerOptions);
        }

        File.Move(temporaryFilePath, filePath, overwrite: true);
    }

    public void Delete(string cacheKey)
    {
        var filePath = GetTokenFilePath(cacheKey);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private string GetTokenFilePath(string cacheKey)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(cacheKey));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.Combine(_rootDirectory, $"{hash}.json");
    }
}