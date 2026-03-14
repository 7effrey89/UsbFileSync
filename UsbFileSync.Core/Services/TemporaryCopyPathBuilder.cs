using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Core.Services;

internal static class TemporaryCopyPathBuilder
{
    public static string Build(string destinationRelativePath)
    {
        var normalizedRelativePath = VolumePath.NormalizeRelativePath(destinationRelativePath);
        var osRelativePath = normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var destinationDirectory = Path.GetDirectoryName(osRelativePath) ?? string.Empty;
        var destinationFileName = Path.GetFileName(osRelativePath);
        var tempFileName = $".{destinationFileName}.{Guid.NewGuid():N}.usfcopy.tmp";
        return string.IsNullOrWhiteSpace(destinationDirectory)
            ? tempFileName
            : VolumePath.NormalizeRelativePath(Path.Combine(destinationDirectory, tempFileName));
    }
}
