namespace UsbFileSync.Core.Services;

internal static class FileTimestampComparer
{
    internal static readonly TimeSpan MatchTolerance = TimeSpan.FromSeconds(2);

    public static bool AreEquivalent(DateTime leftUtc, DateTime rightUtc) =>
        (leftUtc - rightUtc).Duration() <= MatchTolerance;
}

internal sealed record FileSnapshot(string RelativePath, string FullPath, long Length, DateTime LastWriteTimeUtc)
{
    public string Fingerprint => $"{Length}:{LastWriteTimeUtc.Ticks}";

    public bool Matches(FileSnapshot other) =>
        Length == other.Length &&
        FileTimestampComparer.AreEquivalent(LastWriteTimeUtc, other.LastWriteTimeUtc);
}
