using System.Text.Json;

namespace UsbFileSync.Core.Services;

internal sealed class SyncMetadataStore
{
    public const string MetadataDirectoryName = ".sync-metadata";

    private const string FileIndexFileName = "file-index.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public SyncMetadataDocument Load(string rootPath)
    {
        var filePath = GetFilePath(rootPath);
        if (!File.Exists(filePath))
        {
            return new SyncMetadataDocument();
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<SyncMetadataDocument>(stream, SerializerOptions) ?? new SyncMetadataDocument();
        }
        catch (JsonException)
        {
            return new SyncMetadataDocument();
        }
        catch (IOException)
        {
            return new SyncMetadataDocument();
        }
        catch (UnauthorizedAccessException)
        {
            return new SyncMetadataDocument();
        }
    }

    public string GetOrCreateDeviceId(SyncMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!string.IsNullOrWhiteSpace(document.DeviceId))
        {
            return document.DeviceId;
        }

        document.DeviceId = Guid.NewGuid().ToString("N");
        return document.DeviceId;
    }

    public SyncPeerState? GetSharedPeerState(
        SyncMetadataDocument sourceDocument,
        string destinationDeviceId,
        SyncMetadataDocument destinationDocument,
        string sourceDeviceId)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        ArgumentNullException.ThrowIfNull(destinationDocument);

        sourceDocument.PeerStates.TryGetValue(destinationDeviceId, out var sourcePeerState);
        destinationDocument.PeerStates.TryGetValue(sourceDeviceId, out var destinationPeerState);

        return sourcePeerState switch
        {
            null when destinationPeerState is null => null,
            null => destinationPeerState,
            _ when destinationPeerState is null => sourcePeerState,
            _ => sourcePeerState.RecordedAtUtc >= destinationPeerState.RecordedAtUtc
                ? sourcePeerState
                : destinationPeerState,
        };
    }

    public void Save(string rootPath, SyncMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var metadataDirectory = Path.Combine(rootPath, MetadataDirectoryName);
        Directory.CreateDirectory(metadataDirectory);

        var filePath = GetFilePath(rootPath);
        var temporaryPath = $"{filePath}.tmp";

        try
        {
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, document, SerializerOptions);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
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

    private static string GetFilePath(string rootPath) => Path.Combine(rootPath, MetadataDirectoryName, FileIndexFileName);
}

internal sealed class SyncMetadataDocument
{
    public string? DeviceId { get; set; }

    public Dictionary<string, SyncPeerState> PeerStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SyncPeerState
{
    public DateTime RecordedAtUtc { get; set; }

    public Dictionary<string, SyncFileIndexEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SyncFileIndexEntry
{
    public string RelativePath { get; set; } = string.Empty;

    public long? Length { get; set; }

    public DateTime? LastWriteTimeUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public string LastSyncedBy { get; set; } = string.Empty;

    public bool Matches(FileSnapshot snapshot) =>
        !IsDeleted &&
        Length == snapshot.Length &&
        LastWriteTimeUtc == snapshot.LastWriteTimeUtc;
}
