namespace UsbFileSync.Platform.Windows;

internal interface IGoogleDriveClient
{
    Task<GoogleDriveApiClient.GoogleDriveItem> GetEntryAsync(string relativePath, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GoogleDriveApiClient.GoogleDriveItem>> EnumerateAsync(string relativePath, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default);

    Task UploadFileAsync(string relativePath, string localFilePath, bool overwrite = true, CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default);

    Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default);

    Task DeleteDirectoryAsync(string relativePath, CancellationToken cancellationToken = default);

    Task MoveAsync(string sourceRelativePath, string destinationRelativePath, bool overwrite = false, CancellationToken cancellationToken = default);

    Task SetLastWriteTimeUtcAsync(string relativePath, DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default);
}