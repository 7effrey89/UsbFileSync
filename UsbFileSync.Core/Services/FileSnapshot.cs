namespace UsbFileSync.Core.Services;

internal sealed record FileSnapshot(string RelativePath, string FullPath, long Length, DateTime LastWriteTimeUtc)
{
    public string Fingerprint => $"{Length}:{LastWriteTimeUtc.Ticks}";

    public bool Matches(FileSnapshot other) =>
        Length == other.Length && LastWriteTimeUtc == other.LastWriteTimeUtc;
}
