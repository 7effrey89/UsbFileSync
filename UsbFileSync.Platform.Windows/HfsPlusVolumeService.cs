using System.IO;
using DiscUtils.HfsPlus;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public sealed class HfsPlusVolumeService : ISourceVolumeService
{
    private const int AlignedCopyBufferSize = 4096;

    public bool TryCreateVolume(string path, out IVolumeSource? volume, out string? failureReason)
    {
        volume = null;
        failureReason = null;

        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalizedFullPath;
        string normalizedRootPath;
        string relativeDirectoryPath;
        try
        {
            normalizedFullPath = Path.GetFullPath(path);
            normalizedRootPath = NormalizeRootPath(path);
            relativeDirectoryPath = NormalizeRelativeDirectoryPath(normalizedFullPath, normalizedRootPath);
        }
        catch (Exception)
        {
            return false;
        }

        try
        {
            using var stream = OpenRawVolumeStream(normalizedRootPath);
            using var fileSystem = new HfsPlusFileSystem(stream);
            var internalRelativeDirectoryPath = ToInternalPath(relativeDirectoryPath);
            if (!string.IsNullOrEmpty(internalRelativeDirectoryPath) && !fileSystem.DirectoryExists(internalRelativeDirectoryPath))
            {
                failureReason = $"The selected HFS+ folder '{normalizedFullPath}' does not exist.";
                return false;
            }

            var rootVolume = new HfsPlusVolumeSource(normalizedRootPath);
            volume = string.IsNullOrEmpty(relativeDirectoryPath)
                ? rootVolume
                : new SubdirectoryVolumeSource(rootVolume, relativeDirectoryPath);
            return true;
        }
        catch (FileNotFoundException)
        {
            failureReason = $"The selected drive '{normalizedRootPath}' is not currently available. Reconnect the drive and try again.";
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            failureReason = $"The selected drive '{normalizedRootPath}' is not currently available. Reconnect the drive and try again.";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            failureReason = $"The selected drive '{normalizedRootPath}' could not be opened for HFS+ access.";
            return false;
        }
        catch (IOException)
        {
            failureReason = BuildUnsupportedMessage(normalizedRootPath);
            return false;
        }
        catch (Exception)
        {
            failureReason = BuildUnsupportedMessage(normalizedRootPath);
            return false;
        }
    }

    internal static bool IsNotHfsFailure(string? failureReason) =>
        !string.IsNullOrWhiteSpace(failureReason)
        && failureReason.Contains("does not appear to contain an HFS+ volume", StringComparison.OrdinalIgnoreCase);

    private static string BuildUnsupportedMessage(string path) =>
        $"The selected drive '{path}' does not appear to contain an HFS+ volume.";

    private static string NormalizeRelativeDirectoryPath(string fullPath, string rootPath)
    {
        if (string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return fullPath[rootPath.Length..]
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/')
            .Trim('/');
    }

    private static string NormalizeRootPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var rootPath = Path.GetPathRoot(fullPath) ?? fullPath;
        return rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
    }

    private static FileStream OpenRawVolumeStream(string rootPath)
    {
        var devicePath = $@"\\.\{rootPath.TrimEnd(Path.DirectorySeparatorChar)}";
        return new FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    private static string ToInternalPath(string? relativePath) =>
        NormalizeRelativePath(relativePath).Replace('/', '\\');

    private static string NormalizeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        return relativePath
            .Replace('\\', '/')
            .Trim('/');
    }

    private sealed class HfsPlusVolumeSource(string deviceRootPath) : IVolumeSource
    {
        public string Id => $"hfs::{deviceRootPath.TrimEnd(Path.DirectorySeparatorChar)}";

        public string DisplayName => $"HFS+ ({deviceRootPath.TrimEnd(Path.DirectorySeparatorChar)})";

        public string FileSystemType => "HFS+";

        public bool IsReadOnly => true;

        public string Root => deviceRootPath;

        public IFileEntry GetEntry(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return new DiscVolumeFileEntry(Root, DisplayName, true, null, null, true);
            }

            return WithFileSystem(fileSystem =>
            {
                var internalPath = ToInternalPath(normalizedRelativePath);
                var isDirectory = fileSystem.DirectoryExists(internalPath);
                var isFile = fileSystem.FileExists(internalPath);
                if (!isDirectory && !isFile)
                {
                    return new DiscVolumeFileEntry(BuildDisplayPath(normalizedRelativePath), GetLeafName(normalizedRelativePath), false, null, null, false);
                }

                return CreateFileEntry(fileSystem, normalizedRelativePath);
            });
        }

        public IEnumerable<IFileEntry> Enumerate(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            return WithFileSystem(fileSystem =>
            {
                var internalPath = ToInternalPath(normalizedRelativePath);
                if (!string.IsNullOrEmpty(internalPath) && !fileSystem.DirectoryExists(internalPath))
                {
                    return Array.Empty<IFileEntry>();
                }

                return fileSystem.GetFileSystemEntries(internalPath)
                    .Where(entryPath => !string.IsNullOrWhiteSpace(entryPath))
                    .Select(entryPath => NormalizeEntryRelativePath(normalizedRelativePath, entryPath))
                    .Where(entryRelativePath => !string.IsNullOrWhiteSpace(entryRelativePath))
                    .Select(entryRelativePath => CreateFileEntry(fileSystem, entryRelativePath))
                    .Cast<IFileEntry>()
                    .ToArray();
            });
        }

        public Stream OpenRead(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                throw new FileNotFoundException("A file path is required.", BuildDisplayPath(path));
            }

            return WithFileSystem(fileSystem =>
            {
                var internalPath = ToInternalPath(normalizedRelativePath);
                if (!fileSystem.FileExists(internalPath))
                {
                    throw new FileNotFoundException($"The file '{BuildDisplayPath(normalizedRelativePath)}' does not exist.", BuildDisplayPath(normalizedRelativePath));
                }

                var tempPath = Path.GetTempFileName();
                try
                {
                    using var sourceStream = fileSystem.OpenFile(internalPath, FileMode.Open, FileAccess.Read);
                    using (var targetStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        CopyToTempFile(sourceStream, targetStream);
                    }

                    return (Stream)new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose | FileOptions.SequentialScan);
                }
                catch
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }

                    throw;
                }
            });
        }

        public Stream OpenWrite(string path, bool overwrite = true) => throw new ReadOnlyVolumeException(DisplayName);

        public void CreateDirectory(string path) => throw new ReadOnlyVolumeException(DisplayName);

        public void DeleteFile(string path) => throw new ReadOnlyVolumeException(DisplayName);

        public void DeleteDirectory(string path) => throw new ReadOnlyVolumeException(DisplayName);

        public void Move(string sourcePath, string destinationPath, bool overwrite = false) => throw new ReadOnlyVolumeException(DisplayName);

        public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => throw new ReadOnlyVolumeException(DisplayName);

        private T WithFileSystem<T>(Func<HfsPlusFileSystem, T> action)
        {
            using var stream = OpenRawVolumeStream(deviceRootPath);
            using var fileSystem = new HfsPlusFileSystem(stream);
            return action(fileSystem);
        }

        private DiscVolumeFileEntry CreateFileEntry(HfsPlusFileSystem fileSystem, string relativePath)
        {
            var internalPath = ToInternalPath(relativePath);
            var isDirectory = fileSystem.DirectoryExists(internalPath);
            long? size = isDirectory ? null : fileSystem.GetFileLength(internalPath);
            DateTime? lastWriteTimeUtc;
            try
            {
                lastWriteTimeUtc = fileSystem.GetLastWriteTimeUtc(internalPath);
            }
            catch
            {
                lastWriteTimeUtc = null;
            }

            return new DiscVolumeFileEntry(
                BuildDisplayPath(relativePath),
                GetLeafName(relativePath),
                isDirectory,
                size,
                lastWriteTimeUtc,
                true);
        }

        private string BuildDisplayPath(string? relativePath)
        {
            var normalizedRelativePath = NormalizeRelativePath(relativePath);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return Root;
            }

            return Path.Combine(Root, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string NormalizeEntryRelativePath(string parentRelativePath, string entryPath)
        {
            var normalizedEntryPath = NormalizeRelativePath(entryPath);
            var normalizedParent = NormalizeRelativePath(parentRelativePath);

            if (string.IsNullOrEmpty(normalizedParent)
                || string.Equals(normalizedEntryPath, normalizedParent, StringComparison.OrdinalIgnoreCase)
                || normalizedEntryPath.StartsWith(normalizedParent + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedEntryPath;
            }

            return string.IsNullOrEmpty(normalizedEntryPath)
                ? normalizedParent
                : normalizedParent + "/" + normalizedEntryPath;
        }

        private static string NormalizeRelativePath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            return relativePath
                .Replace('\\', '/')
                .Trim('/');
        }

        private static string GetLeafName(string relativePath)
        {
            var normalizedRelativePath = NormalizeRelativePath(relativePath);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return string.Empty;
            }

            var lastSeparatorIndex = normalizedRelativePath.LastIndexOf('/');
            return lastSeparatorIndex < 0 ? normalizedRelativePath : normalizedRelativePath[(lastSeparatorIndex + 1)..];
        }

        private static void CopyToTempFile(Stream sourceStream, Stream targetStream)
        {
            var buffer = new byte[AlignedCopyBufferSize];
            while (true)
            {
                var bytesRead = sourceStream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    break;
                }

                targetStream.Write(buffer, 0, bytesRead);
            }
        }

        private sealed record DiscVolumeFileEntry(
            string FullPath,
            string Name,
            bool IsDirectory,
            long? Size,
            DateTime? LastWriteTimeUtc,
            bool Exists) : IFileEntry;
    }
}
