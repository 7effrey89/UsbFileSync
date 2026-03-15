using System.Net;
using System.Text.Json;

namespace UsbFileSync.Platform.Windows;

internal sealed class GoogleDriveApiClient
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string FilesEndpoint = "https://www.googleapis.com/drive/v3/files";
    private static readonly HttpClient HttpClient = new();
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly GoogleDriveAuthenticationService _authenticationService;

    public GoogleDriveApiClient(GoogleDriveAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
    }

    public async Task<GoogleDriveItem> GetEntryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return GoogleDriveItem.Root;
        }

        var item = await ResolveItemAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);
        return item ?? GoogleDriveItem.NotFound(normalizedRelativePath);
    }

    public async Task<IReadOnlyList<GoogleDriveItem>> EnumerateAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var directory = await GetEntryAsync(relativePath, cancellationToken).ConfigureAwait(false);
        if (!directory.Exists || !directory.IsDirectory)
        {
            return Array.Empty<GoogleDriveItem>();
        }

        return await ListChildrenAsync(directory.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var item = await GetEntryAsync(relativePath, cancellationToken).ConfigureAwait(false);
        if (!item.Exists || item.IsDirectory)
        {
            throw new FileNotFoundException($"The Google Drive file '{relativePath}' does not exist.", relativePath);
        }

        var requestUri = $"{FilesEndpoint}/{Uri.EscapeDataString(item.Id)}?alt=media";
        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, requestUri, cancellationToken).ConfigureAwait(false);
        var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new HttpResponseStream(stream, response);
    }

    private async Task<GoogleDriveItem?> ResolveItemAsync(string normalizedRelativePath, CancellationToken cancellationToken)
    {
        var segments = normalizedRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return GoogleDriveItem.Root;
        }

        var parentId = "root";
        GoogleDriveItem? current = null;
        for (var index = 0; index < segments.Length; index++)
        {
            var isLastSegment = index == segments.Length - 1;
            current = await FindChildByNameAsync(parentId, segments[index], !isLastSegment, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                return null;
            }

            if (!isLastSegment && !current.IsDirectory)
            {
                return null;
            }

            parentId = current.Id;
        }

        return current;
    }

    private async Task<GoogleDriveItem?> FindChildByNameAsync(string parentId, string name, bool requireDirectory, CancellationToken cancellationToken)
    {
        var escapedName = EscapeQueryValue(name);
        var query = $"'{EscapeQueryValue(parentId)}' in parents and trashed = false and name = '{escapedName}'";
        if (requireDirectory)
        {
            query += $" and mimeType = '{FolderMimeType}'";
        }

        var requestUri = $"{FilesEndpoint}?pageSize=10&fields=files(id,name,mimeType,size,modifiedTime)&q={Uri.EscapeDataString(query)}";
        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, requestUri, cancellationToken).ConfigureAwait(false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var fileList = await JsonSerializer.DeserializeAsync<DriveFileListResponse>(payload, SerializerOptions, cancellationToken).ConfigureAwait(false);
        var file = fileList?.Files?.FirstOrDefault();
        return file is null ? null : Map(file);
    }

    private async Task<IReadOnlyList<GoogleDriveItem>> ListChildrenAsync(string parentId, CancellationToken cancellationToken)
    {
        var items = new List<GoogleDriveItem>();
        string? nextPageToken = null;

        do
        {
            var query = $"'{EscapeQueryValue(parentId)}' in parents and trashed = false";
            var requestUri = $"{FilesEndpoint}?pageSize=1000&fields=nextPageToken,files(id,name,mimeType,size,modifiedTime)&orderBy=folder,name_natural&q={Uri.EscapeDataString(query)}";
            if (!string.IsNullOrWhiteSpace(nextPageToken))
            {
                requestUri += $"&pageToken={Uri.EscapeDataString(nextPageToken)}";
            }

            using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, requestUri, cancellationToken).ConfigureAwait(false);
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var fileList = await JsonSerializer.DeserializeAsync<DriveFileListResponse>(payload, SerializerOptions, cancellationToken).ConfigureAwait(false);
            if (fileList?.Files is not null)
            {
                items.AddRange(fileList.Files.Select(Map));
            }

            nextPageToken = fileList?.NextPageToken;
        }
        while (!string.IsNullOrWhiteSpace(nextPageToken));

        return items;
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string requestUri, CancellationToken cancellationToken)
    {
        var accessToken = await _authenticationService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static GoogleDriveItem Map(DriveFile file)
    {
        var isDirectory = string.Equals(file.MimeType, FolderMimeType, StringComparison.OrdinalIgnoreCase);
        return new GoogleDriveItem(
            file.Id ?? string.Empty,
            file.Name ?? string.Empty,
            isDirectory,
            isDirectory ? null : file.Size,
            file.ModifiedTime,
            Exists: true);
    }

    private static string EscapeQueryValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static string NormalizeRelativePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');

    internal sealed record GoogleDriveItem(
        string Id,
        string Name,
        bool IsDirectory,
        long? Size,
        DateTime? LastWriteTimeUtc,
        bool Exists)
    {
        public static GoogleDriveItem Root { get; } = new("root", "Google Drive", true, null, null, true);

        public static GoogleDriveItem NotFound(string relativePath) => new(string.Empty, GetName(relativePath), false, null, null, false);

        private static string GetName(string relativePath)
        {
            var normalizedRelativePath = NormalizeRelativePath(relativePath);
            if (string.IsNullOrEmpty(normalizedRelativePath))
            {
                return string.Empty;
            }

            var separatorIndex = normalizedRelativePath.LastIndexOf('/');
            return separatorIndex >= 0 ? normalizedRelativePath[(separatorIndex + 1)..] : normalizedRelativePath;
        }
    }

    private sealed class HttpResponseStream(Stream innerStream, HttpResponseMessage response) : Stream
    {
        private readonly Stream _innerStream = innerStream;
        private readonly HttpResponseMessage _response = response;

        public override bool CanRead => _innerStream.CanRead;

        public override bool CanSeek => _innerStream.CanSeek;

        public override bool CanWrite => _innerStream.CanWrite;

        public override long Length => _innerStream.Length;

        public override long Position
        {
            get => _innerStream.Position;
            set => _innerStream.Position = value;
        }

        public override void Flush() => _innerStream.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);

        public override void SetLength(long value) => _innerStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _innerStream.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _innerStream.ReadAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _innerStream.Dispose();
                _response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _innerStream.DisposeAsync().ConfigureAwait(false);
            _response.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class DriveFileListResponse
    {
        public List<DriveFile> Files { get; set; } = [];

        public string? NextPageToken { get; set; }
    }

    private sealed class DriveFile
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? MimeType { get; set; }

        public long? Size { get; set; }

        public DateTime? ModifiedTime { get; set; }
    }
}