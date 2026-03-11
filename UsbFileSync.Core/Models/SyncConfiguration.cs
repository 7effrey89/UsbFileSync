namespace UsbFileSync.Core.Models;

public sealed class SyncConfiguration
{
    public string SourcePath { get; init; } = string.Empty;

    public string DestinationPath { get; init; } = string.Empty;

    public SyncMode Mode { get; init; } = SyncMode.OneWay;

    public bool DetectMoves { get; init; } = true;

    public bool DryRun { get; init; }

    public bool VerifyChecksums { get; init; }

    public int ParallelCopyCount { get; init; } = 1;

    public Dictionary<string, string> PreviewProviderMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
