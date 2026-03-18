using System.Globalization;

namespace UsbFileSync.Core.Models;

public sealed record ImageRenamePatternOption(ImageRenamePatternKind Kind, string DisplayName, string Example);

public sealed record ImageRenameScopeOption(string Label, string Value);

public static class ImageRenameDefaults
{
    public static IReadOnlyList<ImageRenamePatternOption> PatternOptions { get; } =
    [
        new(ImageRenamePatternKind.TimestampOriginalFileName, "yyyyMMdd_HHmmss_original_filename.jpg", "20260318_140546_IMG_1234.jpg"),
        new(ImageRenamePatternKind.TimestampOriginalFileNameCity, "yyyyMMdd_HHmmss_original_filename_City.jpg", "20260318_140546_IMG_1234_Berlin.jpg"),
        new(ImageRenamePatternKind.TimestampOnly, "yyyyMMdd_HHmmss.jpg", "20260318_140546.jpg"),
    ];

    public static IReadOnlyList<ImageRenameScopeOption> DefaultCameraFileNamePatterns { get; } =
    [
        new("DSC_???? (Sony / Nikon)", "DSC_????"),
        new("DSCN???? (Nikon)", "DSCN????"),
        new("DSCF???? (Fuji)", "DSCF????"),
        new("DSC????? (Sony / Nikon)", "DSC?????"),
        new("P??????? (Panasonic / Olympus)", "P???????"),
        new("_DSC???? (Sony a6000)", "_DSC????"),
        new("_MG_???? (Canon EOS)", "_MG_????"),
        new("IMG_???? (Canon EOS)", "IMG_????"),
        new("DJI_???? (DJI)", "DJI_????"),
    ];

    public static IReadOnlyList<ImageRenameScopeOption> DefaultExtensions { get; } =
    [
        new("JPEG (.jpg)", ".jpg"),
        new("JPEG (.jpeg)", ".jpeg"),
        new("HEIC (.heic)", ".heic"),
        new("MOV (.mov)", ".mov"),
        new("3GP (.3gp)", ".3gp"),
        new("MP4 (.mp4)", ".mp4"),
    ];

    public static IReadOnlyList<string> GetDefaultCameraFileNameMasks() =>
        DefaultCameraFileNamePatterns.Select(option => option.Value).ToArray();

    public static IReadOnlyList<string> GetDefaultExtensions() =>
        DefaultExtensions.Select(option => option.Value).ToArray();

    public static string NormalizeExtension(string? extension)
    {
        var normalized = (extension ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (!normalized.StartsWith('.', StringComparison.Ordinal))
        {
            normalized = $".{normalized}";
        }

        return normalized.ToLowerInvariant();
    }

    public static string NormalizeFileNameMask(string? mask) =>
        (mask ?? string.Empty).Trim().ToUpperInvariant();

    public static string FormatTimestamp(DateTime timestampLocal) =>
        timestampLocal.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
}
