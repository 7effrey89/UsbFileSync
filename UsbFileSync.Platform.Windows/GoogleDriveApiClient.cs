using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UsbFileSync.Platform.Windows;

internal sealed class GoogleDriveApiClient : IGoogleDriveClient
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string FilesEndpoint = "https://www.googleapis.com/drive/v3/files";
    private const string UploadEndpoint = "https://www.googleapis.com/upload/drive/v3/files";
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

    public async Task UploadFileAsync(string relativePath, string localFilePath, bool overwrite = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        await using var stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, useAsync: true);
        await UploadFileAsync(relativePath, stream, overwrite, cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return;
        }

        _ = await EnsureDirectoryIdAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return;
        }

        var item = await GetEntryAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);
        if (!item.Exists)
        {
            return;
        }

        if (item.IsDirectory)
        {
            throw new IOException($"The Google Drive path '{relativePath}' is a directory.");
        }

        await DeleteItemByIdAsync(item.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return;
        }

        var item = await GetEntryAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);
        if (!item.Exists || !item.IsDirectory)
        {
            return;
        }

        var children = await ListChildrenAsync(item.Id, cancellationToken).ConfigureAwait(false);
        if (children.Count > 0)
        {
            return;
        }

        await DeleteItemByIdAsync(item.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveAsync(string sourceRelativePath, string destinationRelativePath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var normalizedSourcePath = NormalizeRelativePath(sourceRelativePath);
        var normalizedDestinationPath = NormalizeRelativePath(destinationRelativePath);
        if (string.IsNullOrEmpty(normalizedSourcePath))
        {
            throw new IOException("The Google Drive root cannot be moved.");
        }

        if (string.IsNullOrEmpty(normalizedDestinationPath))
        {
            throw new IOException("The Google Drive root cannot be used as a destination file path.");
        }

        var sourceItem = await GetEntryAsync(normalizedSourcePath, cancellationToken).ConfigureAwait(false);
        if (!sourceItem.Exists)
        {
            throw new FileNotFoundException($"The Google Drive item '{sourceRelativePath}' does not exist.", sourceRelativePath);
        }

        var destinationItem = await ResolveItemAsync(normalizedDestinationPath, cancellationToken).ConfigureAwait(false);
        if (destinationItem is not null)
        {
            if (!overwrite)
            {
                throw new IOException($"The Google Drive destination '{destinationRelativePath}' already exists.");
            }

            if (destinationItem.IsDirectory)
            {
                var children = await ListChildrenAsync(destinationItem.Id, cancellationToken).ConfigureAwait(false);
                if (children.Count > 0)
                {
                    throw new IOException($"The Google Drive destination directory '{destinationRelativePath}' is not empty.");
                }
            }

            await DeleteItemByIdAsync(destinationItem.Id, cancellationToken).ConfigureAwait(false);
        }

        var (destinationParentPath, destinationName) = SplitParentAndName(normalizedDestinationPath);
        var destinationParentId = await EnsureDirectoryIdAsync(destinationParentPath, cancellationToken).ConfigureAwait(false);
        var sourceFile = await GetFileByIdAsync(sourceItem.Id, cancellationToken).ConfigureAwait(false);
        var removeParents = sourceFile.Parents is null || sourceFile.Parents.Count == 0
            ? string.Empty
            : string.Join(",", sourceFile.Parents);

        var requestUri = new StringBuilder($"{FilesEndpoint}/{Uri.EscapeDataString(sourceItem.Id)}");
        requestUri.Append("?fields=id,name,mimeType,size,modifiedTime,parents");
        if (!string.IsNullOrWhiteSpace(removeParents))
        {
            requestUri.Append("&removeParents=");
            requestUri.Append(Uri.EscapeDataString(removeParents));
        }

        requestUri.Append("&addParents=");
        requestUri.Append(Uri.EscapeDataString(destinationParentId));

        await PatchJsonAsync(requestUri.ToString(), new { name = destinationName }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetLastWriteTimeUtcAsync(string relativePath, DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return;
        }

        var item = await GetEntryAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);
        if (!item.Exists || item.IsDirectory)
        {
            return;
        }

        await PatchJsonAsync(
            $"{FilesEndpoint}/{Uri.EscapeDataString(item.Id)}?fields=id",
            new { modifiedTime = lastWriteTimeUtc.ToUniversalTime().ToString("O") },
            cancellationToken).ConfigureAwait(false);
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

    private async Task UploadFileAsync(string relativePath, Stream content, bool overwrite, CancellationToken cancellationToken)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            throw new IOException("A Google Drive file path is required for uploads.");
        }

        var (parentRelativePath, fileName) = SplitParentAndName(normalizedRelativePath);
        var parentId = await EnsureDirectoryIdAsync(parentRelativePath, cancellationToken).ConfigureAwait(false);
        var existingItem = await ResolveItemAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);

        if (existingItem is not null)
        {
            if (existingItem.IsDirectory)
            {
                throw new IOException($"The Google Drive destination '{relativePath}' is a directory.");
            }

            if (!overwrite)
            {
                throw new IOException($"The Google Drive destination '{relativePath}' already exists.");
            }

            await UploadExistingFileContentAsync(existingItem.Id, content, cancellationToken).ConfigureAwait(false);
            return;
        }

        await CreateFileAsync(parentId, fileName, content, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> EnsureDirectoryIdAsync(string normalizedRelativePath, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeRelativePath(normalizedRelativePath);
        if (string.IsNullOrEmpty(normalizedPath))
        {
            return "root";
        }

        var existingItem = await ResolveItemAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        if (existingItem is not null)
        {
            if (!existingItem.IsDirectory)
            {
                throw new IOException($"The Google Drive path '{normalizedPath}' is a file, not a directory.");
            }

            return existingItem.Id;
        }

        var (parentRelativePath, name) = SplitParentAndName(normalizedPath);
        var parentId = await EnsureDirectoryIdAsync(parentRelativePath, cancellationToken).ConfigureAwait(false);
        return await CreateFolderAsync(parentId, name, cancellationToken).ConfigureAwait(false);
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
        var matchingFiles = fileList?.Files ?? [];
        if (matchingFiles.Count > 1)
        {
            throw new InvalidOperationException(
                $"Google Drive contains multiple items named '{name}' in the same folder. UsbFileSync currently uses name-based Google Drive paths, so duplicate sibling names are not supported.");
        }

        var file = matchingFiles.FirstOrDefault();
        return file is null ? null : Map(file);
    }

    private async Task<DriveFile> GetFileByIdAsync(string fileId, CancellationToken cancellationToken)
    {
        var requestUri = $"{FilesEndpoint}/{Uri.EscapeDataString(fileId)}?fields=id,name,mimeType,size,modifiedTime,parents";
        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, requestUri, cancellationToken).ConfigureAwait(false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);

        await using var payload = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<DriveFile>(payload, SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Google Drive returned an empty file response.");
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

    private async Task<string> CreateFolderAsync(string parentId, string name, CancellationToken cancellationToken)
    {
        var metadata = new
        {
            name,
            mimeType = FolderMimeType,
            parents = new[] { parentId },
        };

        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, $"{FilesEndpoint}?fields=id,name,mimeType,size,modifiedTime,parents", cancellationToken).ConfigureAwait(false);
        request.Content = CreateJsonContent(metadata);

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);
        await using var payload = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var created = await JsonSerializer.DeserializeAsync<DriveFile>(payload, SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Google Drive did not return the created folder information.");
        return created.Id ?? throw new InvalidOperationException("Google Drive did not return the created folder ID.");
    }

    private async Task CreateFileAsync(string parentId, string name, Stream content, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, $"{UploadEndpoint}?uploadType=multipart&fields=id", cancellationToken).ConfigureAwait(false);
        request.Content = CreateMultipartUploadContent(new { name, parents = new[] { parentId } }, content);

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task UploadExistingFileContentAsync(string fileId, Stream content, CancellationToken cancellationToken)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Patch, $"{UploadEndpoint}/{Uri.EscapeDataString(fileId)}?uploadType=media&fields=id", cancellationToken).ConfigureAwait(false);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task DeleteItemByIdAsync(string itemId, CancellationToken cancellationToken)
    {
        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Delete, $"{FilesEndpoint}/{Uri.EscapeDataString(itemId)}", cancellationToken).ConfigureAwait(false);
        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task PatchJsonAsync(string requestUri, object payload, CancellationToken cancellationToken)
    {
        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Patch, requestUri, cancellationToken).ConfigureAwait(false);
        request.Content = CreateJsonContent(payload);

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessStatusCodeAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string requestUri, CancellationToken cancellationToken)
    {
        var accessToken = await _authenticationService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static HttpContent CreateJsonContent(object payload)
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static HttpContent CreateMultipartUploadContent(object metadata, Stream content)
    {
        var multipart = new MultipartContent("related");
        multipart.Add(CreateJsonContent(metadata));

        var streamContent = new StreamContent(content);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(streamContent);
        return multipart;
    }

    private static async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var payload = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException(BuildFailureMessage(response.StatusCode, payload), null, response.StatusCode);
    }

    private static string BuildFailureMessage(HttpStatusCode statusCode, string payload)
    {
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.TryGetProperty("error", out var errorElement))
                {
                    if (errorElement.ValueKind == JsonValueKind.Object &&
                        errorElement.TryGetProperty("message", out var messageElement) &&
                        messageElement.ValueKind == JsonValueKind.String)
                    {
                        return $"Google Drive request failed ({(int)statusCode}): {messageElement.GetString()}";
                    }

                    if (errorElement.ValueKind == JsonValueKind.String)
                    {
                        return $"Google Drive request failed ({(int)statusCode}): {errorElement.GetString()}";
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return $"Google Drive request failed with status code {(int)statusCode} ({statusCode}).";
    }

    private static (string ParentRelativePath, string Name) SplitParentAndName(string normalizedRelativePath)
    {
        var separatorIndex = normalizedRelativePath.LastIndexOf('/');
        if (separatorIndex < 0)
        {
            return (string.Empty, normalizedRelativePath);
        }

        return (normalizedRelativePath[..separatorIndex], normalizedRelativePath[(separatorIndex + 1)..]);
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

        public List<string> Parents { get; set; } = [];
    }
}