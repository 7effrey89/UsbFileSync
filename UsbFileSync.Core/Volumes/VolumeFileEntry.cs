namespace UsbFileSync.Core.Volumes;

internal sealed record VolumeFileEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long? Size,
    DateTime? LastWriteTimeUtc,
    bool Exists) : IFileEntry;
