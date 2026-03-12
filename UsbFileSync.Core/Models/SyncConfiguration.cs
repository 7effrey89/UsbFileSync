using System.Collections.Generic;
using System.Linq;

namespace UsbFileSync.Core.Models;

public sealed class SyncConfiguration
{
    public string SourcePath { get; init; } = string.Empty;

    public string DestinationPath { get; init; } = string.Empty;

    public IReadOnlyList<string> DestinationPaths { get; init; } = Array.Empty<string>();

    public SyncMode Mode { get; init; } = SyncMode.OneWay;

    public bool DetectMoves { get; init; } = true;

    public bool DryRun { get; init; }

    public bool VerifyChecksums { get; init; }

    public int ParallelCopyCount { get; init; } = 1;

    public Dictionary<string, string> PreviewProviderMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> GetDestinationPaths()
    {
        var normalized = DestinationPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0 && !string.IsNullOrWhiteSpace(DestinationPath))
        {
            normalized.Add(DestinationPath);
        }

        return normalized;
    }
}
