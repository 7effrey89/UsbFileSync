using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsbFileSync.Platform.Windows;

internal sealed class OneDriveApiClient : IOneDriveClient
{
    private const string GraphBaseUri = "https://graph.microsoft.com/v1.0/me/drive";
    private const int UploadChunkSize = 320 * 1024 * 10;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly OneDriveAuthenticationService _authenticationService;
    private readonly HttpClient _httpClient;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, OneDriveItem?> _entryCache = new(StringComparer.OrdinalIgnoreCase)
    {
        [string.Empty] = OneDriveItem.Root,
    };
    private readonly Dictionary<string, IReadOnlyList<OneDriveItem>> _childrenCache = new(StringComparer.OrdinalIgnoreCase);

    public OneDriveApiClient(OneDriveAuthenticationService authenticationService)
        : this(authenticationService, new HttpClient())
    {
    }

    internal OneDriveApiClient(OneDriveAuthenticationService authenticationService, HttpClient httpClient)
    {
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<OneDriveItem> GetEntryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (TryGetCachedEntry(normalizedRelativePath, out var cachedItem))
        {
            return cachedItem ?? OneDriveItem.NotFound(normalizedRelativePath);
        }

        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return OneDriveItem.Root;
        }

        var item = await ResolveItemAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);
        CacheEntry(normalizedRelativePath, item);
        return item ?? OneDriveItem.NotFound(normalizedRelativePath);
    }

    public async Task<IReadOnlyList<OneDriveItem>> EnumerateAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var directory = await GetEntryAsync(relativePath, cancellationToken).ConfigureAwait(false);
        if (!directory.Exists || !directory.IsDirectory)
        {
            return Array.Empty<OneDriveItem>();
        }

        return await ListChildrenAsync(directory.Id, NormalizeRelativePath(relativePath), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var item = await GetEntryAsync(relativePath, cancellationToken).ConfigureAwait(false);
        if (!item.Exists || item.IsDirectory)
        {
            throw new FileNotFoundException($"The OneDrive file '{relativePath}' does not exist.", relativePath);
        }

        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, $"{GraphBaseUri}/items/{Uri.EscapeDataString(item.Id)}/content", cancellationToken).ConfigureAwait(false);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new FileNotFoundException($"The OneDrive file '{relativePath}' does not exist.", relativePath);
        }

        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return new HttpResponseStream(stream, response);
    }

    public async Task UploadFileAsync(string relativePath, string localFilePath, bool overwrite = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFilePath);

        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            throw new IOException("OneDrive uploads require a file path under the OneDrive root.");
        }

        if (!overwrite)
        {
            var existingItem = await GetEntryAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);
            if (existingItem.Exists)
            {
                throw new IOException($"The OneDrive path '{OneDrivePath.BuildPath(normalizedRelativePath)}' already exists.");
            }
        }

        var parentRelativePath = GetParentRelativePath(normalizedRelativePath);
        if (!string.IsNullOrEmpty(parentRelativePath))
        {
            _ = await EnsureDirectoryIdAsync(parentRelativePath, cancellationToken).ConfigureAwait(false);
        }

        var uploadSessionUri = $"{GraphBaseUri}/root:/{BuildEscapedPath(normalizedRelativePath)}:/createUploadSession";
        using var createSessionRequest = await CreateAuthorizedRequestAsync(HttpMethod.Post, uploadSessionUri, cancellationToken).ConfigureAwait(false);
        createSessionRequest.Content = new StringContent(
            JsonSerializer.Serialize(new UploadSessionRequest(
                new UploadSessionItem(overwrite ? "replace" : "fail")), SerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var createSessionResponse = await _httpClient.SendAsync(createSessionRequest, cancellationToken).ConfigureAwait(false);
        var createSessionPayload = await createSessionResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!createSessionResponse.IsSuccessStatusCode)
        {
            throw CreateGraphException(createSessionPayload, "Creating the OneDrive upload session failed.");
        }

        var uploadSession = JsonSerializer.Deserialize<UploadSessionResponse>(createSessionPayload, SerializerOptions)
            ?? throw new InvalidOperationException("OneDrive did not return an upload session.");
        if (string.IsNullOrWhiteSpace(uploadSession.UploadUrl))
        {
            throw new InvalidOperationException("OneDrive did not return a usable upload session URL.");
        }

        await using var stream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, UploadChunkSize, useAsync: true);
        var totalLength = stream.Length;
        var buffer = new byte[UploadChunkSize];
        long offset = 0;
        while (offset < totalLength)
        {
            var bytesToRead = (int)Math.Min(buffer.Length, totalLength - offset);
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken).ConfigureAwait(false);
            if (bytesRead <= 0)
            {
                break;
            }

            using var chunkRequest = new HttpRequestMessage(HttpMethod.Put, uploadSession.UploadUrl)
            {
                Content = new ByteArrayContent(buffer, 0, bytesRead),
            };
            chunkRequest.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + bytesRead - 1, totalLength);
            chunkRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var chunkResponse = await _httpClient.SendAsync(chunkRequest, cancellationToken).ConfigureAwait(false);
            var chunkPayload = await chunkResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!chunkResponse.IsSuccessStatusCode && chunkResponse.StatusCode != HttpStatusCode.Accepted)
            {
                throw CreateGraphException(chunkPayload, "Uploading the OneDrive file failed.");
            }

            offset += bytesRead;
        }

        ClearCache();
    }

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return;
        }

        _ = await EnsureDirectoryIdAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);
        ClearCache();
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
            throw new IOException($"The OneDrive path '{relativePath}' is a directory.");
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

        var children = await ListChildrenAsync(item.Id, normalizedRelativePath, cancellationToken).ConfigureAwait(false);
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
        if (string.IsNullOrEmpty(normalizedSourcePath) || string.IsNullOrEmpty(normalizedDestinationPath))
        {
            throw new IOException("OneDrive moves require both a source path and a destination path.");
        }

        var sourceItem = await GetEntryAsync(normalizedSourcePath, cancellationToken).ConfigureAwait(false);
        if (!sourceItem.Exists)
        {
            throw new FileNotFoundException($"The OneDrive path '{sourceRelativePath}' was not found.", sourceRelativePath);
        }

        var existingDestination = await GetEntryAsync(normalizedDestinationPath, cancellationToken).ConfigureAwait(false);
        if (existingDestination.Exists)
        {
            if (!overwrite)
            {
                throw new IOException($"The OneDrive destination '{destinationRelativePath}' already exists.");
            }

            if (existingDestination.IsDirectory != sourceItem.IsDirectory)
            {
                throw new IOException("The OneDrive destination already exists with a different entry type.");
            }

            await DeleteItemByIdAsync(existingDestination.Id, cancellationToken).ConfigureAwait(false);
        }

        var destinationParentPath = GetParentRelativePath(normalizedDestinationPath);
        var destinationName = GetName(normalizedDestinationPath);
        var destinationParentId = await EnsureDirectoryIdAsync(destinationParentPath, cancellationToken).ConfigureAwait(false);

        using var request = await CreateAuthorizedRequestAsync(new HttpMethod("PATCH"), $"{GraphBaseUri}/items/{Uri.EscapeDataString(sourceItem.Id)}", cancellationToken).ConfigureAwait(false);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new MoveRequest(new ItemParentReference(destinationParentId), destinationName), SerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateGraphException(payload, "Moving the OneDrive item failed.");
        }

        ClearCache();
    }

    public async Task SetLastWriteTimeUtcAsync(string relativePath, DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return;
        }

        var item = await GetEntryAsync(normalizedRelativePath, cancellationToken).ConfigureAwait(false);
        if (!item.Exists)
        {
            throw new FileNotFoundException($"The OneDrive path '{relativePath}' was not found.", relativePath);
        }

        using var request = await CreateAuthorizedRequestAsync(new HttpMethod("PATCH"), $"{GraphBaseUri}/items/{Uri.EscapeDataString(item.Id)}", cancellationToken).ConfigureAwait(false);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new LastWriteTimeRequest(new FileSystemInfoFacet(lastWriteTimeUtc)), SerializerOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateGraphException(payload, "Updating the OneDrive last-write timestamp failed.");
        }

        ClearCache();
    }

    private async Task<OneDriveItem?> ResolveItemAsync(string normalizedRelativePath, CancellationToken cancellationToken)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Get,
            $"{GraphBaseUri}/root:/{BuildEscapedPath(normalizedRelativePath)}?$select=id,name,size,lastModifiedDateTime,folder,file",
            cancellationToken).ConfigureAwait(false);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw CreateGraphException(payload, $"OneDrive path lookup failed for '{normalizedRelativePath}'.");
        }

        var item = JsonSerializer.Deserialize<DriveItem>(payload, SerializerOptions)
            ?? throw new InvalidOperationException("OneDrive returned an empty file entry.");
        return Map(item);
    }

    private async Task<IReadOnlyList<OneDriveItem>> ListChildrenAsync(string itemId, string normalizedRelativePath, CancellationToken cancellationToken)
    {
        if (TryGetCachedChildren(itemId, out var cachedChildren))
        {
            return cachedChildren;
        }

        var requestUri = $"{GraphBaseUri}/items/{Uri.EscapeDataString(itemId)}/children?$select=id,name,size,lastModifiedDateTime,folder,file";
        var items = new List<OneDriveItem>();
        while (!string.IsNullOrWhiteSpace(requestUri))
        {
            using var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, requestUri, cancellationToken).ConfigureAwait(false);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateGraphException(payload, $"Listing OneDrive folder contents failed for '{normalizedRelativePath}'.");
            }

            var listing = JsonSerializer.Deserialize<DriveItemCollectionResponse>(payload, SerializerOptions)
                ?? throw new InvalidOperationException("OneDrive returned an empty folder listing.");
            items.AddRange(listing.Value.Select(Map));
            requestUri = listing.NextLink ?? string.Empty;
        }

        CacheChildren(itemId, items);
        return items;
    }

    private async Task<string> EnsureDirectoryIdAsync(string relativePath, CancellationToken cancellationToken)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return "root";
        }

        var currentPath = string.Empty;
        var currentParentId = "root";
        foreach (var segment in normalizedRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}/{segment}";
            var cachedItem = await GetEntryAsync(currentPath, cancellationToken).ConfigureAwait(false);
            if (cachedItem.Exists)
            {
                if (!cachedItem.IsDirectory)
                {
                    throw new IOException($"The OneDrive path '{OneDrivePath.BuildPath(currentPath)}' already exists as a file.");
                }

                currentParentId = cachedItem.Id;
                continue;
            }

            using var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, $"{GraphBaseUri}/items/{Uri.EscapeDataString(currentParentId)}/children", cancellationToken).ConfigureAwait(false);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new CreateFolderRequest(segment, new FolderFacet(), "replace"), SerializerOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateGraphException(payload, $"Creating the OneDrive folder '{segment}' failed.");
            }

            var createdItem = JsonSerializer.Deserialize<DriveItem>(payload, SerializerOptions)
                ?? throw new InvalidOperationException("OneDrive did not return the created folder details.");
            var mappedItem = Map(createdItem);
            currentParentId = mappedItem.Id;
            CacheEntry(currentPath, mappedItem);
        }

        return currentParentId;
    }

    private async Task DeleteItemByIdAsync(string itemId, CancellationToken cancellationToken)
    {
        using var request = await CreateAuthorizedRequestAsync(HttpMethod.Delete, $"{GraphBaseUri}/items/{Uri.EscapeDataString(itemId)}", cancellationToken).ConfigureAwait(false);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }

        ClearCache();
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string requestUri, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, requestUri);
        var accessToken = await _authenticationService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static string NormalizeRelativePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');

    private static string BuildEscapedPath(string relativePath) =>
        string.Join('/', NormalizeRelativePath(relativePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));

    private static string GetParentRelativePath(string relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return string.Empty;
        }

        var separatorIndex = normalizedRelativePath.LastIndexOf('/');
        return separatorIndex < 0 ? string.Empty : normalizedRelativePath[..separatorIndex];
    }

    private static string GetName(string relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return string.Empty;
        }

        var separatorIndex = normalizedRelativePath.LastIndexOf('/');
        return separatorIndex < 0 ? normalizedRelativePath : normalizedRelativePath[(separatorIndex + 1)..];
    }

    private static OneDriveItem Map(DriveItem item) =>
        new(
            item.Id ?? string.Empty,
            item.Name ?? string.Empty,
            item.Folder is not null,
            item.Size,
            item.LastModifiedDateTime?.UtcDateTime,
            !string.IsNullOrWhiteSpace(item.Id));

    private static Exception CreateGraphException(string payload, string fallbackMessage)
    {
        try
        {
            var error = JsonSerializer.Deserialize<GraphErrorEnvelope>(payload, SerializerOptions)?.Error;
            if (!string.IsNullOrWhiteSpace(error?.Message))
            {
                return new InvalidOperationException(error.Message.Trim());
            }

            if (!string.IsNullOrWhiteSpace(error?.Code))
            {
                return new InvalidOperationException($"{fallbackMessage} {error.Code}".Trim());
            }
        }
        catch (JsonException)
        {
        }

        return new InvalidOperationException(fallbackMessage);
    }

    private bool TryGetCachedEntry(string normalizedRelativePath, out OneDriveItem? item)
    {
        lock (_cacheLock)
        {
            return _entryCache.TryGetValue(normalizedRelativePath, out item);
        }
    }

    private void CacheEntry(string normalizedRelativePath, OneDriveItem? item)
    {
        lock (_cacheLock)
        {
            _entryCache[normalizedRelativePath] = item;
        }
    }

    private bool TryGetCachedChildren(string parentId, out IReadOnlyList<OneDriveItem> children)
    {
        lock (_cacheLock)
        {
            return _childrenCache.TryGetValue(parentId, out children!);
        }
    }

    private void CacheChildren(string parentId, IReadOnlyList<OneDriveItem> children)
    {
        lock (_cacheLock)
        {
            _childrenCache[parentId] = children;
        }
    }

    private void ClearChildrenCache(string parentId)
    {
        lock (_cacheLock)
        {
            _childrenCache.Remove(parentId);
        }
    }

    private void ClearCache()
    {
        lock (_cacheLock)
        {
            _entryCache.Clear();
            _entryCache[string.Empty] = OneDriveItem.Root;
            _childrenCache.Clear();
        }
    }

    internal sealed record OneDriveItem(
        string Id,
        string Name,
        bool IsDirectory,
        long? Size,
        DateTime? LastWriteTimeUtc,
        bool Exists)
    {
        public static OneDriveItem Root { get; } = new("root", "OneDrive", true, null, null, true);

        public static OneDriveItem NotFound(string relativePath) => new(string.Empty, GetName(relativePath), false, null, null, false);
    }

    private sealed class HttpResponseStream(Stream innerStream, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => innerStream.CanRead;
        public override bool CanSeek => innerStream.CanSeek;
        public override bool CanWrite => innerStream.CanWrite;
        public override long Length => innerStream.Length;
        public override long Position { get => innerStream.Position; set => innerStream.Position = value; }
        public override void Flush() => innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => innerStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => innerStream.Seek(offset, origin);
        public override void SetLength(long value) => innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => innerStream.Write(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => innerStream.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => innerStream.WriteAsync(buffer, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => innerStream.WriteAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                innerStream.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await innerStream.DisposeAsync().ConfigureAwait(false);
            response.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed record DriveItemCollectionResponse(
        [property: JsonPropertyName("value")] IReadOnlyList<DriveItem> Value,
        [property: JsonPropertyName("@odata.nextLink")] string? NextLink);

    private sealed record DriveItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("lastModifiedDateTime")] DateTimeOffset? LastModifiedDateTime,
        [property: JsonPropertyName("folder")] FolderFacet? Folder,
        [property: JsonPropertyName("file")] FileFacet? File);

    private sealed record FolderFacet();

    private sealed record FileFacet();

    private sealed record UploadSessionRequest([property: JsonPropertyName("item")] UploadSessionItem Item);

    private sealed record UploadSessionItem([property: JsonPropertyName("@microsoft.graph.conflictBehavior")] string ConflictBehavior);

    private sealed record UploadSessionResponse([property: JsonPropertyName("uploadUrl")] string UploadUrl);

    private sealed record CreateFolderRequest(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("folder")] FolderFacet Folder,
        [property: JsonPropertyName("@microsoft.graph.conflictBehavior")] string ConflictBehavior);

    private sealed record MoveRequest(
        [property: JsonPropertyName("parentReference")] ItemParentReference ParentReference,
        [property: JsonPropertyName("name")] string Name);

    private sealed record ItemParentReference([property: JsonPropertyName("id")] string Id);

    private sealed record LastWriteTimeRequest([property: JsonPropertyName("fileSystemInfo")] FileSystemInfoFacet FileSystemInfo);

    private sealed record FileSystemInfoFacet([property: JsonPropertyName("lastModifiedDateTime")] DateTime LastModifiedDateTime);

    private sealed record GraphErrorEnvelope([property: JsonPropertyName("error")] GraphError? Error);

    private sealed record GraphError(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("message")] string? Message);
}