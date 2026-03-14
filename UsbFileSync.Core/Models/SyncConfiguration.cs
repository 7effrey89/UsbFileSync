using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Core.Models;

public sealed class SyncConfiguration
{
    public string SourcePath { get; init; } = string.Empty;

    [JsonIgnore]
    public IVolumeSource? SourceVolume { get; init; }

    public string DestinationPath { get; init; } = string.Empty;

    [JsonIgnore]
    public IVolumeSource? DestinationVolume { get; init; }

    public IReadOnlyList<string> DestinationPaths { get; init; } = Array.Empty<string>();

    [JsonIgnore]
    public IReadOnlyList<IVolumeSource> DestinationVolumes { get; init; } = Array.Empty<IVolumeSource>();

    public SyncMode Mode { get; init; } = SyncMode.OneWay;

    public bool DetectMoves { get; init; } = true;

    public bool DryRun { get; init; }

    public bool VerifyChecksums { get; init; }

    public bool MoveMode { get; init; }

    public bool HideMacOsSystemFiles { get; init; } = true;

    public int ParallelCopyCount { get; init; } = 1;

    public Dictionary<string, string> PreviewProviderMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> GetDestinationPaths()
    {
        var normalized = DestinationPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0 && DestinationVolumes.Count > 0)
        {
            normalized.AddRange(DestinationVolumes.Select(volume => volume.Root));
        }
        else if (normalized.Count == 0 && DestinationVolume is not null)
        {
            normalized.Add(DestinationVolume.Root);
        }

        if (normalized.Count == 0 && !string.IsNullOrWhiteSpace(DestinationPath))
        {
            normalized.Add(DestinationPath);
        }

        return normalized;
    }
}
