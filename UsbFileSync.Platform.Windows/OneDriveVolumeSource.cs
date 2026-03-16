using UsbFileSync.Core.Volumes;

namespace UsbFileSync.Platform.Windows;

public sealed class OneDriveVolumeSource : IVolumeSource
{
    private readonly IOneDriveClient _apiClient;
    private readonly bool _allowWriteAccess;
    private readonly string _rootPath;
    private readonly string _displayName;
    private readonly string _id;

    internal OneDriveVolumeSource(
        IOneDriveClient apiClient,
        string? registrationId = null,
        string? displayName = null,
        bool allowWriteAccess = false)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _allowWriteAccess = allowWriteAccess;
        _rootPath = OneDrivePath.BuildRootPath(registrationId);
        _displayName = string.IsNullOrWhiteSpace(displayName) ? "OneDrive" : displayName.Trim();
        _id = string.IsNullOrWhiteSpace(registrationId) ? "onedrive::root" : $"onedrive::{registrationId.Trim()}";
    }

    public string Id => _id;

    public string DisplayName => _displayName;

    public string FileSystemType => "OneDrive";

    public bool IsReadOnly => !_allowWriteAccess;

    public string Root => _rootPath;

    public IFileEntry GetEntry(string path)
    {
        var normalizedRelativePath = NormalizeRelativePath(path);
        var item = _apiClient.GetEntryAsync(normalizedRelativePath).GetAwaiter().GetResult();
        return new OneDriveFileEntry(
            FullPath: OneDrivePath.BuildPath(GetRegistrationId(), normalizedRelativePath),
            Name: string.IsNullOrEmpty(normalizedRelativePath) ? DisplayName : item.Name,
            IsDirectory: item.IsDirectory,
            Size: item.Size,
            LastWriteTimeUtc: item.LastWriteTimeUtc,
            Exists: item.Exists);
    }

    public IEnumerable<IFileEntry> Enumerate(string path)
    {
        var normalizedRelativePath = NormalizeRelativePath(path);
        var items = _apiClient.EnumerateAsync(normalizedRelativePath).GetAwaiter().GetResult();
        return items
            .Select(item => (IFileEntry)new OneDriveFileEntry(
                FullPath: OneDrivePath.BuildPath(GetRegistrationId(), CombineRelativePath(normalizedRelativePath, item.Name)),
                Name: item.Name,
                IsDirectory: item.IsDirectory,
                Size: item.Size,
                LastWriteTimeUtc: item.LastWriteTimeUtc,
                Exists: item.Exists))
            .ToArray();
    }

    public Stream OpenRead(string path) => _apiClient.OpenReadAsync(NormalizeRelativePath(path)).GetAwaiter().GetResult();

    public Stream OpenWrite(string path, bool overwrite = true)
    {
        EnsureWritable();
        return new OneDriveWriteStream(_apiClient, NormalizeRelativePath(path), overwrite);
    }

    public void CreateDirectory(string path)
    {
        EnsureWritable();
        _apiClient.CreateDirectoryAsync(NormalizeRelativePath(path)).GetAwaiter().GetResult();
    }

    public void DeleteFile(string path)
    {
        EnsureWritable();
        _apiClient.DeleteFileAsync(NormalizeRelativePath(path)).GetAwaiter().GetResult();
    }

    public void DeleteDirectory(string path)
    {
        EnsureWritable();
        _apiClient.DeleteDirectoryAsync(NormalizeRelativePath(path)).GetAwaiter().GetResult();
    }

    public void Move(string sourcePath, string destinationPath, bool overwrite = false)
    {
        EnsureWritable();
        _apiClient.MoveAsync(NormalizeRelativePath(sourcePath), NormalizeRelativePath(destinationPath), overwrite).GetAwaiter().GetResult();
    }

    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
    {
        EnsureWritable();
        _apiClient.SetLastWriteTimeUtcAsync(NormalizeRelativePath(path), lastWriteTimeUtc).GetAwaiter().GetResult();
    }

    private void EnsureWritable()
    {
        if (IsReadOnly)
        {
            throw new ReadOnlyVolumeException(DisplayName);
        }
    }

    private static string NormalizeRelativePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');

    private static string CombineRelativePath(string parentRelativePath, string childName) =>
        string.IsNullOrEmpty(parentRelativePath)
            ? childName
            : $"{parentRelativePath}/{childName}";

    private string? GetRegistrationId()
    {
        OneDrivePath.TryParse(_rootPath, out var registrationId, out _);
        return registrationId;
    }

    private sealed record OneDriveFileEntry(
        string FullPath,
        string Name,
        bool IsDirectory,
        long? Size,
        DateTime? LastWriteTimeUtc,
        bool Exists) : IFileEntry;

    private sealed class OneDriveWriteStream : Stream
    {
        private readonly IOneDriveClient _apiClient;
        private readonly string _relativePath;
        private readonly bool _overwrite;
        private readonly string _temporaryFilePath;
        private readonly FileStream _innerStream;
        private bool _committed;

        public OneDriveWriteStream(IOneDriveClient apiClient, string relativePath, bool overwrite)
        {
            _apiClient = apiClient;
            _relativePath = relativePath;
            _overwrite = overwrite;

            var tempDirectory = Path.Combine(Path.GetTempPath(), "UsbFileSync", "OneDriveUploads");
            Directory.CreateDirectory(tempDirectory);
            _temporaryFilePath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.tmp");
            _innerStream = new FileStream(_temporaryFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1024 * 128, useAsync: true);
        }

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }
        public override void Flush() => _innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _innerStream.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _innerStream.WriteAsync(buffer, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _innerStream.WriteAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (!disposing)
            {
                base.Dispose(false);
                return;
            }

            Exception? disposalException = null;
            try
            {
                if (!_committed)
                {
                    _innerStream.Dispose();
                    _apiClient.UploadFileAsync(_relativePath, _temporaryFilePath, _overwrite).GetAwaiter().GetResult();
                    _committed = true;
                }
            }
            catch (Exception exception)
            {
                disposalException = exception;
            }
            finally
            {
                try
                {
                    _innerStream.Dispose();
                }
                catch (Exception exception) when (disposalException is null)
                {
                    disposalException = exception;
                }

                try
                {
                    if (File.Exists(_temporaryFilePath))
                    {
                        File.Delete(_temporaryFilePath);
                    }
                }
                catch (Exception exception) when (disposalException is null)
                {
                    disposalException = exception;
                }
            }

            if (disposalException is not null)
            {
                throw disposalException;
            }

            base.Dispose(true);
        }

        public override async ValueTask DisposeAsync()
        {
            Exception? disposalException = null;
            try
            {
                if (!_committed)
                {
                    await _innerStream.DisposeAsync().ConfigureAwait(false);
                    await _apiClient.UploadFileAsync(_relativePath, _temporaryFilePath, _overwrite).ConfigureAwait(false);
                    _committed = true;
                }
            }
            catch (Exception exception)
            {
                disposalException = exception;
            }
            finally
            {
                try
                {
                    await _innerStream.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (disposalException is null)
                {
                    disposalException = exception;
                }

                try
                {
                    if (File.Exists(_temporaryFilePath))
                    {
                        File.Delete(_temporaryFilePath);
                    }
                }
                catch (Exception exception) when (disposalException is null)
                {
                    disposalException = exception;
                }
            }

            if (disposalException is not null)
            {
                throw disposalException;
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}