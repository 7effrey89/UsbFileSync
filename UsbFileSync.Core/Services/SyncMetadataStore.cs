using System.Text.Json;
using System.Text.Json.Serialization;

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

    public string GetRootDisplayName(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return string.Empty;
        }

        try
        {
            var fullPath = Path.GetFullPath(rootPath);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return fullPath;
            }

            var normalizedFullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(normalizedFullPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            var driveInfo = new DriveInfo(root);
            var driveText = normalizedRoot;
            var label = string.IsNullOrWhiteSpace(driveInfo.VolumeLabel)
                ? GetDefaultDriveLabel(driveInfo.DriveType)
                : driveInfo.VolumeLabel.Trim();

            return string.IsNullOrWhiteSpace(label)
                ? driveText
                : $"{label} ({driveText})";
        }
        catch (IOException)
        {
            return rootPath;
        }
        catch (UnauthorizedAccessException)
        {
            return rootPath;
        }
        catch (ArgumentException)
        {
            return rootPath;
        }
    }

    private static string GetDefaultDriveLabel(DriveType driveType) => driveType switch
    {
        DriveType.Fixed => "Local Disk",
        DriveType.Removable => "USB Drive",
        DriveType.Network => "Network Drive",
        DriveType.CDRom => "CD Drive",
        DriveType.Ram => "RAM Disk",
        _ => string.Empty,
    };

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
