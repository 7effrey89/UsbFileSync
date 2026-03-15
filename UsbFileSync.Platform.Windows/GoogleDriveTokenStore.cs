using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UsbFileSync.Platform.Windows;

internal sealed class GoogleDriveTokenStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _rootDirectory;

    public GoogleDriveTokenStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsbFileSync", "GoogleDriveTokens")
            : rootDirectory;
    }

    public GoogleDriveAuthToken? Load(string clientId)
    {
        var filePath = GetTokenFilePath(clientId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<GoogleDriveAuthToken>(stream, SerializerOptions);
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

    public void Save(string clientId, GoogleDriveAuthToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(token);

        Directory.CreateDirectory(_rootDirectory);
        var filePath = GetTokenFilePath(clientId);
        var temporaryFilePath = filePath + ".tmp";
        using (var stream = File.Create(temporaryFilePath))
        {
            JsonSerializer.Serialize(stream, token, SerializerOptions);
        }

        File.Move(temporaryFilePath, filePath, overwrite: true);
    }

    public void Delete(string clientId)
    {
        var filePath = GetTokenFilePath(clientId);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private string GetTokenFilePath(string clientId)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(clientId));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.Combine(_rootDirectory, $"{hash}.json");
    }
}