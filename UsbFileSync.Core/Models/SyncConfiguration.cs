namespace UsbFileSync.Core.Models;

public sealed class SyncConfiguration
{
    public string SourcePath { get; init; } = string.Empty;

    public string DestinationPath { get; init; } = string.Empty;

    public SyncMode Mode { get; init; } = SyncMode.OneWay;

    public bool DetectMoves { get; init; } = true;

    public bool DryRun { get; init; }
}
