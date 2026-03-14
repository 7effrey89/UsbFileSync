using System.IO;
using System.Diagnostics;
using DiscUtils.Ext;
using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public sealed class ExtVolumeService : ISourceVolumeService
{
    private readonly bool _allowWriteAccess;
    private readonly Func<bool> _hasRequiredWriteAccess;
    private readonly Func<string, (bool Success, int DiskNumber)> _resolvePhysicalDrive;

    public ExtVolumeService(bool allowWriteAccess = false)
        : this(allowWriteAccess, WindowsWriteAccess.HasRequiredWriteAccess, WindowsPhysicalDriveResolver.ResolveDiskNumber)
    {
    }

    internal ExtVolumeService(
        bool allowWriteAccess,
        Func<bool> hasRequiredWriteAccess,
        Func<string, (bool Success, int DiskNumber)> resolvePhysicalDrive)
    {
        _allowWriteAccess = allowWriteAccess;
        _hasRequiredWriteAccess = hasRequiredWriteAccess ?? throw new ArgumentNullException(nameof(hasRequiredWriteAccess));
        _resolvePhysicalDrive = resolvePhysicalDrive ?? throw new ArgumentNullException(nameof(resolvePhysicalDrive));
    }

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

        if (HasAccessibleExistingAncestor(normalizedFullPath))
        {
            return false;
        }

        string? writableFailureReason = null;
        if (TryCreateWritableVolume(normalizedFullPath, normalizedRootPath, relativeDirectoryPath, out volume, out writableFailureReason))
        {
            failureReason = null;
            return true;
        }

        if (TryCreateReadOnlyVolume(normalizedFullPath, normalizedRootPath, relativeDirectoryPath, out volume, out failureReason))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(failureReason))
        {
            failureReason = writableFailureReason;
        }

        return false;
    }

    private bool TryCreateWritableVolume(
        string normalizedFullPath,
        string normalizedRootPath,
        string relativeDirectoryPath,
        out IVolumeSource? volume,
        out string? failureReason)
    {
        volume = null;
        failureReason = null;

        if (!_allowWriteAccess || !_hasRequiredWriteAccess())
        {
            return false;
        }

        var physicalDrive = _resolvePhysicalDrive(normalizedRootPath);
        if (!physicalDrive.Success)
        {
            return false;
        }

        try
        {
            using var mountedVolume = OpenWritableVolume(physicalDrive.DiskNumber);
            var internalRelativeDirectoryPath = ToSharpPath(relativeDirectoryPath);
            if (internalRelativeDirectoryPath != "/" && !mountedVolume.FileSystem.DirectoryExists(internalRelativeDirectoryPath))
            {
                failureReason = $"The selected ext4 folder '{normalizedFullPath}' does not exist.";
                return false;
            }

            var rootVolume = new ExtWritableVolumeSource(normalizedRootPath, physicalDrive.DiskNumber);
            volume = string.IsNullOrEmpty(relativeDirectoryPath)
                ? rootVolume
                : new SubdirectoryVolumeSource(rootVolume, relativeDirectoryPath);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            failureReason = $"The selected drive '{normalizedRootPath}' could not be opened for writable ext4 access.";
            return false;
        }
        catch (IOException ex)
        {
            failureReason = ex.Message;
            return false;
        }
        catch (Exception)
        {
            failureReason = BuildUnsupportedMessage(normalizedRootPath);
            return false;
        }
    }

    private static bool TryCreateReadOnlyVolume(
        string normalizedFullPath,
        string normalizedRootPath,
        string relativeDirectoryPath,
        out IVolumeSource? volume,
        out string? failureReason)
    {
        volume = null;
        failureReason = null;

        try
        {
            using var mountedVolume = OpenMountedVolume(normalizedRootPath);
            var internalRelativeDirectoryPath = ToDiscUtilsPath(relativeDirectoryPath);
            if (!string.IsNullOrEmpty(internalRelativeDirectoryPath) && !mountedVolume.FileSystem.DirectoryExists(internalRelativeDirectoryPath))
            {
                failureReason = $"The selected ext4 folder '{normalizedFullPath}' does not exist.";
                return false;
            }

            var rootVolume = new ExtReadOnlyVolumeSource(normalizedRootPath);
            volume = string.IsNullOrEmpty(relativeDirectoryPath)
                ? rootVolume
                : new SubdirectoryVolumeSource(rootVolume, relativeDirectoryPath);
            return true;
        }
        catch (FileNotFoundException)
        {
            failureReason = null;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            failureReason = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            failureReason = $"The selected drive '{normalizedRootPath}' could not be opened for ext4 access.";
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

    private static string BuildUnsupportedMessage(string path) =>
        $"The selected drive '{path}' does not appear to contain an ext2/ext3/ext4 volume.";

    private static bool HasAccessibleExistingAncestor(string fullPath)
    {
        var current = fullPath;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(current))
            {
                return true;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        return false;
    }

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

    private static MountedDiscUtilsVolume OpenMountedVolume(string rootPath)
    {
        var baseStream = OpenRawVolumeStream(rootPath);
        var alignedStream = new SectorAlignedReadStream(baseStream, 512);

        try
        {
            var fileSystem = new ExtFileSystem(alignedStream);
            return new MountedDiscUtilsVolume(baseStream, alignedStream, fileSystem);
        }
        catch
        {
            alignedStream.Dispose();
            throw;
        }
    }

    private static MountedSharpExtVolume OpenWritableVolume(int diskNumber)
    {
        var disk = SharpExt4DiskAccessor.OpenDiskWithRetry(diskNumber);
        try
        {
            var fileSystem = OpenWritableFileSystemWithRetry(disk, diskNumber);
            return new MountedSharpExtVolume(disk, fileSystem);
        }
        catch
        {
            disk.Dispose();
            throw;
        }
    }

    internal static SharpExt4.ExtFileSystem OpenWritableFileSystemWithRetry(
        SharpExt4.ExtDisk disk,
        int diskNumber,
        Func<SharpExt4.ExtDisk, int, SharpExt4.ExtFileSystem>? openFileSystem = null,
        int maxAttempts = 3,
        int delayMilliseconds = 250,
        Action<int>? delay = null)
    {
        openFileSystem ??= OpenWritableFileSystem;
        delay ??= Thread.Sleep;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return openFileSystem(disk, diskNumber);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientWritableMountFailure(ex))
            {
                Trace.TraceWarning($"Retrying SharpExt4 mount for PhysicalDrive{diskNumber} after transient partition registration failure ({attempt}/{maxAttempts}). {ex.Message}");
                delay(delayMilliseconds);
            }
        }

        return openFileSystem(disk, diskNumber);
    }

    private static SharpExt4.ExtFileSystem OpenWritableFileSystem(SharpExt4.ExtDisk disk, int diskNumber)
    {
        if (disk.Partitions is null || disk.Partitions.Count == 0)
        {
            throw new IOException("The selected drive does not expose any partitions that can be inspected.");
        }

        var mountFailures = new List<string>();
        for (var index = 0; index < disk.Partitions.Count; index++)
        {
            var partition = disk.Partitions[index];
            try
            {
                return SharpExt4.ExtFileSystem.Open(disk, partition);
            }
            catch (Exception ex)
            {
                mountFailures.Add($"{DescribePartition(index, partition)}: {ex.Message}");
            }
        }

        throw new IOException(
            $"SharpExt4 could not mount any partition on PhysicalDrive{diskNumber}. Partitions tried: {string.Join("; ", mountFailures)}");
    }

    internal static bool IsTransientWritableMountFailure(Exception exception) =>
        exception is IOException ioException &&
        ioException.Message.Contains("Could not register partition", StringComparison.OrdinalIgnoreCase);

    private static string DescribePartition(int index, SharpExt4.Partition partition) =>
        $"partition {index + 1} (offset {partition.Offset} bytes, size {FormatBytes(partition.Size)})";

    private static string ToDiscUtilsPath(string? relativePath) =>
        NormalizeRelativePath(relativePath).Replace('/', '\\');

    private static string ToSharpPath(string? relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        return string.IsNullOrEmpty(normalizedRelativePath)
            ? "/"
            : "/" + normalizedRelativePath;
    }

    private static string FromSharpPath(string? path) =>
        NormalizeRelativePath(path);

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

    private static string BuildDisplayPath(string root, string? relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return root;
        }

        return Path.Combine(root, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetLeafName(string? relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return string.Empty;
        }

        var lastSeparatorIndex = normalizedRelativePath.LastIndexOf('/');
        return lastSeparatorIndex < 0 ? normalizedRelativePath : normalizedRelativePath[(lastSeparatorIndex + 1)..];
    }

    private static DiscVolumeFileEntry BuildRootEntry(string root, string displayName) =>
        new(root, displayName, true, null, null, true);

    private static string FormatBytes(ulong bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var index = 0;

        while (size >= 1024 && index < suffixes.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:F2} {suffixes[index]}";
    }

    private sealed class ExtReadOnlyVolumeSource(string deviceRootPath) : IVolumeSource
    {
        public string Id => $"ext::{deviceRootPath.TrimEnd(Path.DirectorySeparatorChar)}";

        public string DisplayName => $"ext4 ({deviceRootPath.TrimEnd(Path.DirectorySeparatorChar)})";

        public string FileSystemType => "ext4";

        public bool IsReadOnly => true;

        public string Root => deviceRootPath;

        public IFileEntry GetEntry(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return BuildRootEntry(Root, DisplayName);
            }

            return WithFileSystem(fileSystem =>
            {
                var internalPath = ToDiscUtilsPath(normalizedRelativePath);
                var isDirectory = fileSystem.DirectoryExists(internalPath);
                var isFile = fileSystem.FileExists(internalPath);
                if (!isDirectory && !isFile)
                {
                    return new DiscVolumeFileEntry(BuildDisplayPath(Root, normalizedRelativePath), GetLeafName(normalizedRelativePath), false, null, null, false);
                }

                return CreateFileEntry(fileSystem, normalizedRelativePath);
            });
        }

        public IEnumerable<IFileEntry> Enumerate(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            return WithFileSystem(fileSystem =>
            {
                var internalPath = ToDiscUtilsPath(normalizedRelativePath);
                if (!string.IsNullOrEmpty(internalPath) && !fileSystem.DirectoryExists(internalPath))
                {
                    return Array.Empty<IFileEntry>();
                }

                return fileSystem.GetFileSystemEntries(internalPath)
                    .Where(entryPath => !string.IsNullOrWhiteSpace(entryPath))
                    .Select(entryPath => NormalizeRelativePath(entryPath))
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
                throw new FileNotFoundException("A file path is required.", BuildDisplayPath(Root, path));
            }

            var mountedVolume = OpenMountedVolume(deviceRootPath);
            try
            {
                var internalPath = ToDiscUtilsPath(normalizedRelativePath);
                if (!mountedVolume.FileSystem.FileExists(internalPath))
                {
                    throw new FileNotFoundException($"The file '{BuildDisplayPath(Root, normalizedRelativePath)}' does not exist.", BuildDisplayPath(Root, normalizedRelativePath));
                }

                return new OwnedVolumeStream(mountedVolume.FileSystem.OpenFile(internalPath, FileMode.Open, FileAccess.Read), mountedVolume);
            }
            catch
            {
                mountedVolume.Dispose();
                throw;
            }
        }

        public Stream OpenWrite(string path, bool overwrite = true) => throw new ReadOnlyVolumeException(DisplayName);

        public void CreateDirectory(string path) => throw new ReadOnlyVolumeException(DisplayName);

        public void DeleteFile(string path) => throw new ReadOnlyVolumeException(DisplayName);

        public void DeleteDirectory(string path) => throw new ReadOnlyVolumeException(DisplayName);

        public void Move(string sourcePath, string destinationPath, bool overwrite = false) => throw new ReadOnlyVolumeException(DisplayName);

        public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => throw new ReadOnlyVolumeException(DisplayName);

        private T WithFileSystem<T>(Func<ExtFileSystem, T> action)
        {
            using var mountedVolume = OpenMountedVolume(deviceRootPath);
            return action(mountedVolume.FileSystem);
        }

        private DiscVolumeFileEntry CreateFileEntry(ExtFileSystem fileSystem, string relativePath)
        {
            var internalPath = ToDiscUtilsPath(relativePath);
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
                BuildDisplayPath(Root, relativePath),
                GetLeafName(relativePath),
                isDirectory,
                size,
                lastWriteTimeUtc,
                true);
        }
    }

    private sealed class ExtWritableVolumeSource(string deviceRootPath, int diskNumber) : IVolumeSource
    {
        public string Id => $"ext::{deviceRootPath.TrimEnd(Path.DirectorySeparatorChar)}::physical::{diskNumber}";

        public string DisplayName => $"ext4 ({deviceRootPath.TrimEnd(Path.DirectorySeparatorChar)})";

        public string FileSystemType => "ext4";

        public bool IsReadOnly => false;

        public string Root => deviceRootPath;

        public IFileEntry GetEntry(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return BuildRootEntry(Root, DisplayName);
            }

            return WithFileSystem(fileSystem =>
            {
                var sharpPath = ToSharpPath(normalizedRelativePath);
                var isDirectory = fileSystem.DirectoryExists(sharpPath);
                var isFile = fileSystem.FileExists(sharpPath);
                if (!isDirectory && !isFile)
                {
                    return new DiscVolumeFileEntry(BuildDisplayPath(Root, normalizedRelativePath), GetLeafName(normalizedRelativePath), false, null, null, false);
                }

                return CreateFileEntry(fileSystem, normalizedRelativePath);
            });
        }

        public IEnumerable<IFileEntry> Enumerate(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            return WithFileSystem(fileSystem =>
            {
                var sharpPath = ToSharpPath(normalizedRelativePath);
                if (sharpPath != "/" && !fileSystem.DirectoryExists(sharpPath))
                {
                    return Array.Empty<IFileEntry>();
                }

                var directories = fileSystem.GetDirectories(sharpPath, "*", SearchOption.TopDirectoryOnly)
                    .Select(FromSharpPath)
                    .Where(entryRelativePath => !string.IsNullOrWhiteSpace(entryRelativePath))
                    .Select(entryRelativePath => CreateFileEntry(fileSystem, entryRelativePath));

                var files = fileSystem.GetFiles(sharpPath, "*", SearchOption.TopDirectoryOnly)
                    .Select(FromSharpPath)
                    .Where(entryRelativePath => !string.IsNullOrWhiteSpace(entryRelativePath))
                    .Select(entryRelativePath => CreateFileEntry(fileSystem, entryRelativePath));

                return directories.Concat(files).Cast<IFileEntry>().ToArray();
            });
        }

        public Stream OpenRead(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                throw new FileNotFoundException("A file path is required.", BuildDisplayPath(Root, path));
            }

            var mountedVolume = OpenWritableVolume(diskNumber);
            try
            {
                var sharpPath = ToSharpPath(normalizedRelativePath);
                if (!mountedVolume.FileSystem.FileExists(sharpPath))
                {
                    throw new FileNotFoundException($"The file '{BuildDisplayPath(Root, normalizedRelativePath)}' does not exist.", BuildDisplayPath(Root, normalizedRelativePath));
                }

                return new OwnedVolumeStream(mountedVolume.FileSystem.OpenFile(sharpPath, FileMode.Open, FileAccess.Read), mountedVolume);
            }
            catch
            {
                mountedVolume.Dispose();
                throw;
            }
        }

        public Stream OpenWrite(string path, bool overwrite = true)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            var mountedVolume = OpenWritableVolume(diskNumber);

            try
            {
                var sharpPath = ToSharpPath(normalizedRelativePath);
                EnsureParentDirectoryExists(mountedVolume.FileSystem, sharpPath);
                if (overwrite && mountedVolume.FileSystem.FileExists(sharpPath))
                {
                    mountedVolume.FileSystem.DeleteFile(sharpPath);
                }

                var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
                return new OwnedVolumeStream(mountedVolume.FileSystem.OpenFile(sharpPath, mode, FileAccess.Write), mountedVolume);
            }
            catch
            {
                mountedVolume.Dispose();
                throw;
            }
        }

        public void CreateDirectory(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return;
            }

            WithFileSystem(fileSystem =>
            {
                fileSystem.CreateDirectory(ToSharpPath(normalizedRelativePath));
                return 0;
            });
        }

        public void DeleteFile(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return;
            }

            WithFileSystem(fileSystem =>
            {
                var sharpPath = ToSharpPath(normalizedRelativePath);
                if (fileSystem.FileExists(sharpPath))
                {
                    fileSystem.DeleteFile(sharpPath);
                }

                return 0;
            });
        }

        public void DeleteDirectory(string path)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return;
            }

            WithFileSystem(fileSystem =>
            {
                var sharpPath = ToSharpPath(normalizedRelativePath);
                if (fileSystem.DirectoryExists(sharpPath))
                {
                    fileSystem.DeleteDirectory(sharpPath);
                }

                return 0;
            });
        }

        public void Move(string sourcePath, string destinationPath, bool overwrite = false)
        {
            var normalizedSourcePath = NormalizeRelativePath(sourcePath);
            var normalizedDestinationPath = NormalizeRelativePath(destinationPath);

            WithFileSystem(fileSystem =>
            {
                var sourceSharpPath = ToSharpPath(normalizedSourcePath);
                var destinationSharpPath = ToSharpPath(normalizedDestinationPath);
                EnsureParentDirectoryExists(fileSystem, destinationSharpPath);

                if (fileSystem.FileExists(sourceSharpPath))
                {
                    if (overwrite && fileSystem.FileExists(destinationSharpPath))
                    {
                        fileSystem.DeleteFile(destinationSharpPath);
                    }

                    fileSystem.RenameFile(sourceSharpPath, destinationSharpPath);
                    return 0;
                }

                if (fileSystem.DirectoryExists(sourceSharpPath))
                {
                    if (overwrite && fileSystem.DirectoryExists(destinationSharpPath))
                    {
                        fileSystem.DeleteDirectory(destinationSharpPath);
                    }

                    fileSystem.MoveDirectory(sourceSharpPath, destinationSharpPath);
                }

                return 0;
            });
        }

        public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
        {
            var normalizedRelativePath = NormalizeRelativePath(path);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return;
            }

            WithFileSystem(fileSystem =>
            {
                var sharpPath = ToSharpPath(normalizedRelativePath);
                if (fileSystem.FileExists(sharpPath))
                {
                    fileSystem.SetLastWriteTime(sharpPath, lastWriteTimeUtc.ToLocalTime());
                }

                return 0;
            });
        }

        private T WithFileSystem<T>(Func<SharpExt4.ExtFileSystem, T> action)
        {
            using var mountedVolume = OpenWritableVolume(diskNumber);
            return action(mountedVolume.FileSystem);
        }

        private DiscVolumeFileEntry CreateFileEntry(SharpExt4.ExtFileSystem fileSystem, string relativePath)
        {
            var sharpPath = ToSharpPath(relativePath);
            var isDirectory = fileSystem.DirectoryExists(sharpPath);
            ulong? size = isDirectory ? null : fileSystem.GetFileLength(sharpPath);
            DateTime? lastWriteTimeUtc;
            try
            {
                lastWriteTimeUtc = fileSystem.GetLastWriteTime(sharpPath).ToUniversalTime();
            }
            catch
            {
                lastWriteTimeUtc = null;
            }

            return new DiscVolumeFileEntry(
                BuildDisplayPath(Root, relativePath),
                GetLeafName(relativePath),
                isDirectory,
                size is null ? null : (long?)size.Value,
                lastWriteTimeUtc,
                true);
        }

        private static void EnsureParentDirectoryExists(SharpExt4.ExtFileSystem fileSystem, string sharpPath)
        {
            var parentPath = GetParentSharpPath(sharpPath);
            if (parentPath != "/" && !fileSystem.DirectoryExists(parentPath))
            {
                fileSystem.CreateDirectory(parentPath);
            }
        }

        private static string GetParentSharpPath(string sharpPath)
        {
            var normalizedSharpPath = string.IsNullOrWhiteSpace(sharpPath)
                ? "/"
                : sharpPath.Replace('\\', '/');

            if (normalizedSharpPath == "/")
            {
                return "/";
            }

            var lastSeparatorIndex = normalizedSharpPath.LastIndexOf('/');
            return lastSeparatorIndex <= 0 ? "/" : normalizedSharpPath[..lastSeparatorIndex];
        }
    }

    internal sealed class SectorAlignedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _sectorSize;

        public SectorAlignedReadStream(Stream inner, int sectorSize)
        {
            ArgumentNullException.ThrowIfNull(inner);
            if (sectorSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sectorSize));
            }

            _inner = inner;
            _sectorSize = sectorSize;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (count == 0)
            {
                return 0;
            }

            var start = _inner.Position;
            var alignedStart = start - (start % _sectorSize);
            var prefix = (int)(start - alignedStart);
            var alignedCount = AlignUp(prefix + count, _sectorSize);

            byte[] temp = new byte[alignedCount];
            _inner.Position = alignedStart;
            var read = _inner.Read(temp, 0, alignedCount);
            var available = Math.Max(0, Math.Min(count, read - prefix));

            Array.Copy(temp, prefix, buffer, offset, available);
            _inner.Position = start + available;
            return available;
        }

        public override int Read(Span<byte> buffer)
        {
            byte[] temp = new byte[buffer.Length];
            var read = Read(temp, 0, temp.Length);
            temp.AsSpan(0, read).CopyTo(buffer);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private static int AlignUp(int value, int alignment) =>
            ((value + alignment - 1) / alignment) * alignment;
    }

    private sealed class MountedDiscUtilsVolume : IDisposable
    {
        private readonly Stream _baseStream;
        private readonly SectorAlignedReadStream _alignedStream;

        public MountedDiscUtilsVolume(Stream baseStream, SectorAlignedReadStream alignedStream, ExtFileSystem fileSystem)
        {
            _baseStream = baseStream;
            _alignedStream = alignedStream;
            FileSystem = fileSystem;
        }

        public ExtFileSystem FileSystem { get; }

        public void Dispose()
        {
            FileSystem.Dispose();
            _alignedStream.Dispose();
            _baseStream.Dispose();
        }
    }

    private sealed class MountedSharpExtVolume : IDisposable
    {
        private readonly SharpExt4.ExtDisk _disk;

        public MountedSharpExtVolume(SharpExt4.ExtDisk disk, SharpExt4.ExtFileSystem fileSystem)
        {
            _disk = disk;
            FileSystem = fileSystem;
        }

        public SharpExt4.ExtFileSystem FileSystem { get; }

        public void Dispose()
        {
            FileSystem.Dispose();
            _disk.Dispose();
        }
    }

    private sealed class OwnedVolumeStream : Stream
    {
        private readonly Stream _inner;
        private readonly IDisposable _owner;

        public OwnedVolumeStream(Stream inner, IDisposable owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => _inner.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _owner.Dispose();
            }

            base.Dispose(disposing);
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
