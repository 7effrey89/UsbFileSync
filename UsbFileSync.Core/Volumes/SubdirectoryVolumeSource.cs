namespace UsbFileSync.Core.Volumes;

public sealed class SubdirectoryVolumeSource : IVolumeSource
{
    private readonly IVolumeSource _innerVolume;
    private readonly string _rootRelativePath;

    public SubdirectoryVolumeSource(IVolumeSource innerVolume, string rootRelativePath)
    {
        ArgumentNullException.ThrowIfNull(innerVolume);

        var normalizedRootRelativePath = VolumePath.NormalizeRelativePath(rootRelativePath);
        if (string.IsNullOrWhiteSpace(normalizedRootRelativePath))
        {
            throw new ArgumentException("A subdirectory path is required.", nameof(rootRelativePath));
        }

        var rootEntry = innerVolume.GetEntry(normalizedRootRelativePath);
        if (!rootEntry.Exists || !rootEntry.IsDirectory)
        {
            throw new DirectoryNotFoundException($"The directory '{VolumePath.CombineDisplayPath(innerVolume, normalizedRootRelativePath)}' does not exist on volume '{innerVolume.DisplayName}'.");
        }

        _innerVolume = innerVolume;
        _rootRelativePath = normalizedRootRelativePath;
        Id = $"{innerVolume.Id}::{_rootRelativePath}";
        DisplayName = string.IsNullOrWhiteSpace(innerVolume.DisplayName)
            ? _rootRelativePath
            : $"{innerVolume.DisplayName} / {_rootRelativePath}";
        FileSystemType = innerVolume.FileSystemType;
        IsReadOnly = innerVolume.IsReadOnly;
        Root = VolumePath.CombineDisplayPath(innerVolume, _rootRelativePath);
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string FileSystemType { get; }

    public bool IsReadOnly { get; }

    public string Root { get; }

    public IFileEntry GetEntry(string path)
    {
        var normalizedRelativePath = VolumePath.NormalizeRelativePath(path);
        var innerPath = CombineInnerPath(normalizedRelativePath);
        var entry = _innerVolume.GetEntry(innerPath);
        if (!entry.Exists)
        {
            return new WrappedFileEntry(VolumePath.CombineDisplayPath(this, normalizedRelativePath), VolumePath.GetName(normalizedRelativePath), false, null, null, false);
        }

        return WrapEntry(entry, normalizedRelativePath);
    }

    public IEnumerable<IFileEntry> Enumerate(string path)
    {
        var normalizedRelativePath = VolumePath.NormalizeRelativePath(path);
        var innerPath = CombineInnerPath(normalizedRelativePath);
        return _innerVolume.Enumerate(innerPath)
            .Select(entry =>
            {
                var childRelativePath = string.IsNullOrEmpty(normalizedRelativePath)
                    ? entry.Name
                    : $"{normalizedRelativePath}/{entry.Name}";
                return (IFileEntry)WrapEntry(entry, VolumePath.NormalizeRelativePath(childRelativePath));
            })
            .ToArray();
    }

    public Stream OpenRead(string path) => _innerVolume.OpenRead(CombineInnerPath(path));

    public Stream OpenWrite(string path, bool overwrite = true) => _innerVolume.OpenWrite(CombineInnerPath(path), overwrite);

    public void CreateDirectory(string path) => _innerVolume.CreateDirectory(CombineInnerPath(path));

    public void DeleteFile(string path) => _innerVolume.DeleteFile(CombineInnerPath(path));

    public void DeleteDirectory(string path) => _innerVolume.DeleteDirectory(CombineInnerPath(path));

    public void Move(string sourcePath, string destinationPath, bool overwrite = false) =>
        _innerVolume.Move(CombineInnerPath(sourcePath), CombineInnerPath(destinationPath), overwrite);

    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) =>
        _innerVolume.SetLastWriteTimeUtc(CombineInnerPath(path), lastWriteTimeUtc);

    private string CombineInnerPath(string? relativePath)
    {
        var normalizedRelativePath = VolumePath.NormalizeRelativePath(relativePath);
        return string.IsNullOrEmpty(normalizedRelativePath)
            ? _rootRelativePath
            : $"{_rootRelativePath}/{normalizedRelativePath}";
    }

    private WrappedFileEntry WrapEntry(IFileEntry entry, string relativePath)
    {
        var wrappedName = string.IsNullOrEmpty(relativePath) ? entry.Name : VolumePath.GetName(relativePath);
        return new WrappedFileEntry(
            VolumePath.CombineDisplayPath(this, relativePath),
            wrappedName,
            entry.IsDirectory,
            entry.Size,
            entry.LastWriteTimeUtc,
            entry.Exists);
    }

    private sealed record WrappedFileEntry(
        string FullPath,
        string Name,
        bool IsDirectory,
        long? Size,
        DateTime? LastWriteTimeUtc,
        bool Exists) : IFileEntry;
}