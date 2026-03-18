using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UsbFileSync.Core.Models;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Core.Services;

public sealed class ImageRenameAnalysisService
{
    public ImageRenameAnalysisResult Analyze(
        IVolumeSource volume,
        bool hideMacOsSystemFiles,
        IReadOnlyList<string>? excludedPathPatterns,
        bool includeSubfolders,
        ImageRenamePatternKind patternKind,
        IReadOnlyList<string>? enabledFileNameMasks,
        IReadOnlyList<string>? enabledExtensions)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var fileNameMasks = NormalizeFileNameMasks(enabledFileNameMasks);
        var extensions = NormalizeExtensions(enabledExtensions);
        var snapshots = DirectorySnapshotBuilder.Build(volume, hideMacOsSystemFiles, excludedPathPatterns, includeSubfolders: includeSubfolders);
        var occupiedRelativePaths = snapshots.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var compiledMasks = fileNameMasks
            .Select(mask => new KeyValuePair<string, Regex>(mask, CreateMaskRegex(mask)))
            .ToArray();

        var plans = new List<ImageRenamePlanItem>();
        var candidateCount = 0;

        foreach (var snapshot in snapshots.Values.OrderBy(snapshot => snapshot.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var extension = ImageRenameDefaults.NormalizeExtension(Path.GetExtension(snapshot.RelativePath));
            if (!extensions.Contains(extension))
            {
                continue;
            }

            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(snapshot.RelativePath);
            var matchedMask = compiledMasks
                .FirstOrDefault(entry => entry.Value.IsMatch(fileNameWithoutExtension));

            if (string.IsNullOrWhiteSpace(matchedMask.Key))
            {
                continue;
            }

            candidateCount++;

            var proposedFileName = BuildProposedFileName(snapshot.LastWriteTimeUtc.ToLocalTime(), fileNameWithoutExtension, extension, patternKind, city: null);
            var proposedRelativePath = BuildUniqueRelativePath(snapshot.RelativePath, proposedFileName, occupiedRelativePaths);

            if (string.Equals(proposedRelativePath, snapshot.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            occupiedRelativePaths.Add(proposedRelativePath);
            plans.Add(new ImageRenamePlanItem(
                snapshot.RelativePath,
                snapshot.FullPath,
                Path.GetFileName(snapshot.RelativePath),
                Path.GetFileName(proposedRelativePath),
                proposedRelativePath,
                matchedMask.Key,
                snapshot.LastWriteTimeUtc.ToLocalTime(),
                !string.Equals(Path.GetFileName(proposedRelativePath), proposedFileName, StringComparison.OrdinalIgnoreCase)));
        }

        return new ImageRenameAnalysisResult(plans, snapshots.Count, candidateCount);
    }

    public int ApplyRenames(IVolumeSource volume, IEnumerable<ImageRenamePlanItem> plannedRenames, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(plannedRenames);

        var appliedCount = 0;
        foreach (var plan in plannedRenames.OrderBy(plan => plan.SourceRelativePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            volume.Move(plan.SourceRelativePath, plan.ProposedRelativePath, overwrite: false);
            appliedCount++;
        }

        return appliedCount;
    }

    internal static string BuildProposedFileName(
        DateTime timestampLocal,
        string originalFileNameWithoutExtension,
        string extension,
        ImageRenamePatternKind patternKind,
        string? city)
    {
        var timestamp = ImageRenameDefaults.FormatTimestamp(timestampLocal);
        var normalizedExtension = ImageRenameDefaults.NormalizeExtension(extension);
        var originalNameSegment = SanitizeSegment(originalFileNameWithoutExtension);
        var citySegment = SanitizeSegment(city);

        var fileNameWithoutExtension = patternKind switch
        {
            ImageRenamePatternKind.TimestampOriginalFileName => $"{timestamp}_{originalNameSegment}",
            ImageRenamePatternKind.TimestampOriginalFileNameCity when !string.IsNullOrWhiteSpace(citySegment) => $"{timestamp}_{originalNameSegment}_{citySegment}",
            ImageRenamePatternKind.TimestampOriginalFileNameCity => $"{timestamp}_{originalNameSegment}",
            ImageRenamePatternKind.TimestampOnly => timestamp,
            _ => $"{timestamp}_{originalNameSegment}",
        };

        return $"{fileNameWithoutExtension}{normalizedExtension}";
    }

    internal static string BuildUniqueRelativePath(string sourceRelativePath, string proposedFileName, ISet<string> occupiedRelativePaths)
    {
        var directoryPath = GetDirectoryPath(sourceRelativePath);
        var baseName = Path.GetFileNameWithoutExtension(proposedFileName);
        var extension = Path.GetExtension(proposedFileName);
        var candidateRelativePath = CombineRelativePath(directoryPath, proposedFileName);
        if (!occupiedRelativePaths.Contains(candidateRelativePath))
        {
            return candidateRelativePath;
        }

        for (var sequence = 1; sequence < 1000; sequence++)
        {
            var candidateFileName = $"{baseName}_{sequence.ToString("000", CultureInfo.InvariantCulture)}{extension}";
            candidateRelativePath = CombineRelativePath(directoryPath, candidateFileName);
            if (!occupiedRelativePaths.Contains(candidateRelativePath))
            {
                return candidateRelativePath;
            }
        }

        throw new IOException($"Unable to find a free filename for '{sourceRelativePath}'.");
    }

    private static HashSet<string> NormalizeExtensions(IReadOnlyList<string>? enabledExtensions)
    {
        var normalized = (enabledExtensions ?? ImageRenameDefaults.GetDefaultExtensions())
            .Select(ImageRenameDefaults.NormalizeExtension)
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (normalized.Count == 0)
        {
            normalized.UnionWith(ImageRenameDefaults.GetDefaultExtensions());
        }

        return normalized;
    }

    private static List<string> NormalizeFileNameMasks(IReadOnlyList<string>? enabledFileNameMasks)
    {
        var normalized = (enabledFileNameMasks ?? ImageRenameDefaults.GetDefaultCameraFileNameMasks())
            .Select(ImageRenameDefaults.NormalizeFileNameMask)
            .Where(mask => !string.IsNullOrWhiteSpace(mask))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            normalized.AddRange(ImageRenameDefaults.GetDefaultCameraFileNameMasks());
        }

        return normalized;
    }

    private static Regex CreateMaskRegex(string mask)
    {
        var pattern = "^" + Regex.Escape(mask)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";

        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = false;
        foreach (var character in value.Trim())
        {
            var normalizedCharacter = invalidCharacters.Contains(character) || char.IsWhiteSpace(character)
                ? '_'
                : character;

            if (normalizedCharacter == '_')
            {
                if (lastWasSeparator)
                {
                    continue;
                }

                lastWasSeparator = true;
            }
            else
            {
                lastWasSeparator = false;
            }

            builder.Append(normalizedCharacter);
        }

        return builder.ToString().Trim('_');
    }

    private static string GetDirectoryPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : normalized[..separatorIndex];
    }

    private static string CombineRelativePath(string directoryPath, string fileName) =>
        string.IsNullOrWhiteSpace(directoryPath)
            ? fileName
            : $"{directoryPath.TrimEnd('/')}/{fileName}";
}
