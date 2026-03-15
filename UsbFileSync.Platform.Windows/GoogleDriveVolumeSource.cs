using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public sealed class GoogleDriveVolumeSource : IVolumeSource
{
    private readonly GoogleDriveApiClient _apiClient;

    internal GoogleDriveVolumeSource(GoogleDriveApiClient apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public string Id => "gdrive::root";

    public string DisplayName => "Google Drive";

    public string FileSystemType => "Google Drive";

    public bool IsReadOnly => true;

    public string Root => GoogleDrivePath.RootPath;

    public IFileEntry GetEntry(string path)
    {
        var normalizedRelativePath = NormalizeRelativePath(path);
        var item = _apiClient.GetEntryAsync(normalizedRelativePath).GetAwaiter().GetResult();
        return new GoogleDriveFileEntry(
            FullPath: GoogleDrivePath.BuildPath(normalizedRelativePath),
            Name: string.IsNullOrEmpty(normalizedRelativePath) ? DisplayName : item.Name,
            IsDirectory: item.IsDirectory,
            Size: item.Size,
            LastWriteTimeUtc: item.LastWriteTimeUtc,
            Exists: item.Exists);
    }

    public IEnumerable<IFileEntry> Enumerate(string path)
    {
        var normalizedRelativePath = NormalizeRelativePath(path);
        return _apiClient.EnumerateAsync(normalizedRelativePath).GetAwaiter().GetResult()
            .Select(item => (IFileEntry)new GoogleDriveFileEntry(
                FullPath: GoogleDrivePath.BuildPath(CombineRelativePath(normalizedRelativePath, item.Name)),
                Name: item.Name,
                IsDirectory: item.IsDirectory,
                Size: item.Size,
                LastWriteTimeUtc: item.LastWriteTimeUtc,
                Exists: item.Exists))
            .ToArray();
    }

    public Stream OpenRead(string path) => _apiClient.OpenReadAsync(NormalizeRelativePath(path)).GetAwaiter().GetResult();

    public Stream OpenWrite(string path, bool overwrite = true) => throw new ReadOnlyVolumeException(DisplayName);

    public void CreateDirectory(string path) => throw new ReadOnlyVolumeException(DisplayName);

    public void DeleteFile(string path) => throw new ReadOnlyVolumeException(DisplayName);

    public void DeleteDirectory(string path) => throw new ReadOnlyVolumeException(DisplayName);

    public void Move(string sourcePath, string destinationPath, bool overwrite = false) => throw new ReadOnlyVolumeException(DisplayName);

    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => throw new ReadOnlyVolumeException(DisplayName);

    private static string NormalizeRelativePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');

    private static string CombineRelativePath(string parentRelativePath, string childName) =>
        string.IsNullOrEmpty(parentRelativePath)
            ? childName
            : $"{parentRelativePath}/{childName}";

    private sealed record GoogleDriveFileEntry(
        string FullPath,
        string Name,
        bool IsDirectory,
        long? Size,
        DateTime? LastWriteTimeUtc,
        bool Exists) : IFileEntry;
}