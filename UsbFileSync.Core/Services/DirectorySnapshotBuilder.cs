using UsbFileSync.Core.Models;

namespace UsbFileSync.Core.Services;

internal static class DirectorySnapshotBuilder
{
    public static IReadOnlyDictionary<string, FileSnapshot> Build(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A sync path is required.", nameof(rootPath));
        }

        if (!Directory.Exists(rootPath))
        {
            return new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var relativePath = Path.GetRelativePath(rootPath, path);
                return new FileSnapshot(relativePath, path, info.Length, info.LastWriteTimeUtc);
            })
            .OrderBy(snapshot => snapshot.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(snapshot => snapshot.RelativePath, snapshot => snapshot, StringComparer.OrdinalIgnoreCase);

        return files;
    }

    public static void EnsureConfigurationIsValid(SyncConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(configuration.SourcePath))
        {
            throw new ArgumentException("SourcePath is required.", nameof(configuration));
        }

        if (string.IsNullOrWhiteSpace(configuration.DestinationPath))
        {
            throw new ArgumentException("DestinationPath is required.", nameof(configuration));
        }
    }
}
