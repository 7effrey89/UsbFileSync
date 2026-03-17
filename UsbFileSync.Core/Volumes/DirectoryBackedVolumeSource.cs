namespace UsbFileSync.Core.Volumes;

public abstract class DirectoryBackedVolumeSource : IVolumeSource
{
    private readonly string _backingRoot;
    private readonly bool _useWindowsDisplayPaths;

    protected DirectoryBackedVolumeSource(
        string id,
        string displayName,
        string fileSystemType,
        bool isReadOnly,
        string root,
        string backingRoot,
        bool useWindowsDisplayPaths)
    {
        if (string.IsNullOrWhiteSpace(backingRoot))
        {
            throw new ArgumentException("A backing root is required.", nameof(backingRoot));
        }

        Id = id;
        DisplayName = displayName;
        FileSystemType = fileSystemType;
        IsReadOnly = isReadOnly;
        Root = root;
        _backingRoot = Path.GetFullPath(backingRoot);
        _useWindowsDisplayPaths = useWindowsDisplayPaths;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string FileSystemType { get; }

    public bool IsReadOnly { get; }

    public string Root { get; }

    public IFileEntry GetEntry(string path)
    {
        var normalizedRelativePath = VolumePath.NormalizeRelativePath(path);
        var backingPath = ToBackingPath(normalizedRelativePath);
        var displayPath = ToDisplayPath(normalizedRelativePath);
        var name = string.IsNullOrEmpty(normalizedRelativePath)
            ? (string.IsNullOrWhiteSpace(DisplayName) ? Root : DisplayName)
            : VolumePath.GetName(normalizedRelativePath);

        if (Directory.Exists(backingPath))
        {
            var directoryInfo = new DirectoryInfo(backingPath);
            return new VolumeFileEntry(displayPath, name, true, null, directoryInfo.LastWriteTimeUtc, true);
        }

        if (File.Exists(backingPath))
        {
            var fileInfo = new FileInfo(backingPath);
            return new VolumeFileEntry(displayPath, name, false, fileInfo.Length, fileInfo.LastWriteTimeUtc, true);
        }

        return new VolumeFileEntry(displayPath, name, false, null, null, false);
    }

    public IEnumerable<IFileEntry> Enumerate(string path)
    {
        var normalizedRelativePath = VolumePath.NormalizeRelativePath(path);
        var directoryPath = ToBackingPath(normalizedRelativePath);
        if (!Directory.Exists(directoryPath))
        {
            return Array.Empty<IFileEntry>();
        }

        try
        {
            // Use DirectoryInfo.EnumerateFileSystemInfos to retrieve metadata (size,
            // timestamps) from the single FindFirstFile/FindNextFile OS call, avoiding a
            // separate stat per entry that the previous GetEntry path required.
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = (FileAttributes)0,
            };
            return new DirectoryInfo(directoryPath)
                .EnumerateFileSystemInfos("*", options)
                .Select(info =>
                {
                    var childRelativePath = Path.GetRelativePath(_backingRoot, info.FullName)
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');
                    var displayPath = ToDisplayPath(childRelativePath);
                    var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
                    long? size = isDirectory ? null : ((FileInfo)info).Length;
                    return (IFileEntry)new VolumeFileEntry(
                        displayPath, info.Name, isDirectory, size, info.LastWriteTimeUtc, true);
                })
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<IFileEntry>();
        }
        catch (IOException)
        {
            return Array.Empty<IFileEntry>();
        }
    }

    public Stream OpenRead(string path)
    {
        var entry = GetEntry(path);
        if (!entry.Exists || entry.IsDirectory)
        {
            throw new FileNotFoundException($"The file '{entry.FullPath}' does not exist.", entry.FullPath);
        }

        return new FileStream(ToBackingPath(path), FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, useAsync: true);
    }

    public Stream OpenWrite(string path, bool overwrite = true)
    {
        EnsureWritable();
        var backingPath = ToBackingPath(path);
        var directoryPath = Path.GetDirectoryName(backingPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        return new FileStream(backingPath, mode, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true);
    }

    public void CreateDirectory(string path)
    {
        EnsureWritable();
        Directory.CreateDirectory(ToBackingPath(path));
    }

    public void DeleteFile(string path)
    {
        EnsureWritable();
        var backingPath = ToBackingPath(path);
        if (File.Exists(backingPath))
        {
            File.Delete(backingPath);
        }
    }

    public void DeleteDirectory(string path)
    {
        EnsureWritable();
        var backingPath = ToBackingPath(path);
        if (Directory.Exists(backingPath) && !Directory.EnumerateFileSystemEntries(backingPath).Any())
        {
            Directory.Delete(backingPath, recursive: false);
        }
    }

    public void Move(string sourcePath, string destinationPath, bool overwrite = false)
    {
        EnsureWritable();
        var sourceBackingPath = ToBackingPath(sourcePath);
        var destinationBackingPath = ToBackingPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(destinationBackingPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (overwrite && File.Exists(destinationBackingPath))
        {
            File.Delete(destinationBackingPath);
        }

        if (File.Exists(sourceBackingPath))
        {
            File.Move(sourceBackingPath, destinationBackingPath);
        }
    }

    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
    {
        EnsureWritable();
        var backingPath = ToBackingPath(path);
        if (File.Exists(backingPath))
        {
            File.SetLastWriteTimeUtc(backingPath, lastWriteTimeUtc);
        }
    }

    private string ToBackingPath(string? relativePath)
    {
        var normalizedRelativePath = VolumePath.NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return _backingRoot;
        }

        var osRelativePath = normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_backingRoot, osRelativePath);
    }

    private string ToDisplayPath(string? relativePath)
    {
        var normalizedRelativePath = VolumePath.NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return Root;
        }

        if (_useWindowsDisplayPaths)
        {
            var osRelativePath = normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(Root, osRelativePath);
        }

        return $"{Root.TrimEnd('/', '\\')}/{normalizedRelativePath}";
    }

    protected virtual void EnsureWritable()
    {
        if (IsReadOnly)
        {
            throw new ReadOnlyVolumeException(DisplayName);
        }
    }
}
