namespace UsbFileSync.Core.Models;

public sealed record DuplicateFileCandidate(
    string ItemKey,
    string DuplicateRelativePath,
    string DuplicateFullPath,
    long Length,
    DateTime LastWriteTimeUtc,
    string KeepRelativePath,
    string KeepFullPath,
    string ChecksumSha256);
