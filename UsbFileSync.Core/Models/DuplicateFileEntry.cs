namespace UsbFileSync.Core.Models;

public sealed record DuplicateFileEntry(
    string ItemKey,
    string RelativePath,
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc,
    string ChecksumSha256);
