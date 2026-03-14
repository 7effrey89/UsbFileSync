using System.Text.Json;
using System.Text.Json.Serialization;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Core.Services;

internal sealed class SyncMetadataStore
{
    public const string MetadataDirectoryName = ".sync-metadata";

    private const string FileIndexFileName = "file-index.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public SyncMetadataDocument Load(string rootPath) =>
        Load(new WindowsMountedVolume(rootPath));

    public SyncMetadataDocument Load(IVolumeSource volume)
    {
        var fileEntry = volume.GetEntry(GetRelativeFilePath());
        if (!fileEntry.Exists || fileEntry.IsDirectory)
        {
            return new SyncMetadataDocument();
        }

        try
        {
            using var stream = volume.OpenRead(GetRelativeFilePath());
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

    public string GetOrCreateRootId(SyncMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!string.IsNullOrWhiteSpace(document.RootId))
        {
            return document.RootId;
        }

        document.RootId = Guid.NewGuid().ToString("N");
        return document.RootId;
    }

    public string GetRootDisplayName(string rootPath) =>
        GetRootDisplayName(new WindowsMountedVolume(rootPath));

    public string GetRootDisplayName(IVolumeSource volume) => volume.DisplayName;

    public SyncPeerState? GetSharedPeerState(
        SyncMetadataDocument sourceDocument,
        string destinationRootId,
        SyncMetadataDocument destinationDocument,
        string sourceRootId)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        ArgumentNullException.ThrowIfNull(destinationDocument);

        sourceDocument.PeerStates.TryGetValue(destinationRootId, out var sourcePeerState);
        destinationDocument.PeerStates.TryGetValue(sourceRootId, out var destinationPeerState);

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

    public void Save(string rootPath, SyncMetadataDocument document) =>
        Save(new WindowsMountedVolume(rootPath), document);

    public void Save(IVolumeSource volume, SyncMetadataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (volume.IsReadOnly)
        {
            return;
        }

        var metadataDirectory = MetadataDirectoryName;
        var filePath = GetRelativeFilePath();
        var temporaryPath = $"{filePath}.tmp";

        volume.CreateDirectory(metadataDirectory);

        try
        {
            using (var stream = volume.OpenWrite(temporaryPath, overwrite: true))
            {
                JsonSerializer.Serialize(stream, document, SerializerOptions);
            }

            volume.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            try
            {
                volume.DeleteFile(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string GetRelativeFilePath() => VolumePath.NormalizeRelativePath(Path.Combine(MetadataDirectoryName, FileIndexFileName));
}

internal sealed class SyncMetadataDocument
{
    public string? RootId { get; set; }

    public string? RootName { get; set; }

    public Dictionary<string, SyncPeerState> PeerStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class SyncPeerState
{
    public string PeerRootName { get; set; } = string.Empty;

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

    public string LastSyncedByRootId { get; set; } = string.Empty;

    public string LastSyncedByRootName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChecksumSha256 { get; set; }

    public bool Matches(FileSnapshot snapshot) =>
        !IsDeleted &&
        Length == snapshot.Length &&
        LastWriteTimeUtc is { } lastWriteTimeUtc &&
        FileTimestampComparer.AreEquivalent(lastWriteTimeUtc, snapshot.LastWriteTimeUtc);
}
