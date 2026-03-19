namespace UsbFileSync.Core.Models;

public sealed record DuplicateFileGroup(
    string GroupKey,
    string ChecksumSha256,
    long Length,
    IReadOnlyList<DuplicateFileEntry> Files);
