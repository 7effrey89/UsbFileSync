using System.Security.Cryptography;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Core.Services;

public sealed class DuplicateFileAnalysisService
{
    public async Task<DuplicateAnalysisResult> AnalyzeAsync(
        IVolumeSource volume,
        bool hideMacOsSystemFiles,
        IReadOnlyList<string>? excludedPathPatterns,
        bool includeSubfolders,
        CancellationToken cancellationToken = default,
        IProgress<DuplicateAnalyzeProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var files = DirectorySnapshotBuilder.Build(
            volume,
            hideMacOsSystemFiles,
            excludedPathPatterns,
            scanObserver: null,
            includeSubfolders: includeSubfolders);

        cancellationToken.ThrowIfCancellationRequested();

        var sizeCandidateGroups = files.Values
            .GroupBy(file => file.Length)
            .Select(group => group.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToList())
            .Where(group => group.Count > 1)
            .ToList();

        var totalFilesToHash = sizeCandidateGroups.Sum(group => group.Count);
        var hashedFiles = 0;
        var duplicateGroups = 0;
        var candidates = new List<DuplicateFileCandidate>();

        foreach (var sizeGroup in sizeCandidateGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var checksumGroups = new Dictionary<string, List<FileSnapshot>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in sizeGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var checksum = await ComputeChecksumAsync(volume, file.RelativePath, cancellationToken).ConfigureAwait(false);
                hashedFiles++;
                progress?.Report(new DuplicateAnalyzeProgress(file.FullPath, hashedFiles, totalFilesToHash, duplicateGroups));

                if (!checksumGroups.TryGetValue(checksum, out var group))
                {
                    group = [];
                    checksumGroups[checksum] = group;
                }

                group.Add(file);
            }

            foreach (var checksumGroup in checksumGroups.Where(pair => pair.Value.Count > 1))
            {
                cancellationToken.ThrowIfCancellationRequested();

                duplicateGroups++;
                var orderedFiles = checksumGroup.Value
                    .OrderBy(file => file.LastWriteTimeUtc)
                    .ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var keepFile = orderedFiles[0];
                foreach (var duplicateFile in orderedFiles.Skip(1))
                {
                    candidates.Add(new DuplicateFileCandidate(
                        ItemKey: $"{duplicateFile.RelativePath}|{duplicateFile.FullPath}|{keepFile.FullPath}",
                        DuplicateRelativePath: duplicateFile.RelativePath,
                        DuplicateFullPath: duplicateFile.FullPath,
                        Length: duplicateFile.Length,
                        LastWriteTimeUtc: duplicateFile.LastWriteTimeUtc,
                        KeepRelativePath: keepFile.RelativePath,
                        KeepFullPath: keepFile.FullPath,
                        ChecksumSha256: checksumGroup.Key));
                }
            }
        }

        return new DuplicateAnalysisResult(candidates, duplicateGroups, hashedFiles);
    }

    public int DeleteDuplicates(
        IVolumeSource volume,
        IEnumerable<DuplicateFileCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(candidates);

        var deletedCount = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            volume.DeleteFile(candidate.DuplicateRelativePath);
            deletedCount++;
        }

        return deletedCount;
    }

    private static async Task<string> ComputeChecksumAsync(
        IVolumeSource volume,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using var stream = volume.OpenRead(relativePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
