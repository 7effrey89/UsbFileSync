using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsbFileSync.Platform.Windows;

internal sealed class DropboxApiClient : IDropboxClient
{
    private const string ApiBaseUrl = "https://api.dropboxapi.com/2";
    private const string ContentBaseUrl = "https://content.dropboxapi.com/2";
    private const string MissingScopeReauthenticationMessage = "Dropbox permissions changed for this app, so UsbFileSync cleared the cached Dropbox sign-in. Retry the action and complete Dropbox sign-in once more.";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly DropboxAuthenticationService _authenticationService;
    private readonly HttpClient _httpClient;

    public DropboxApiClient(DropboxAuthenticationService authenticationService, HttpClient? httpClient = null)
    {
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<DropboxItem> GetEntryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrEmpty(normalizedRelativePath))
        {
            return DropboxItem.Root;
        }

        var response = await SendApiRequestAsync(
            cancellationToken => CreateApiRequestAsync("/files/get_metadata", new { path = ToDropboxPath(normalizedRelativePath) }, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var payload = response.Payload;
        if (response.StatusCode == HttpStatusCode.Conflict && payload.Contains("not_found", StringComparison.OrdinalIgnoreCase))
        {
            return DropboxItem.NotFound(normalizedRelativePath);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw CreateDropboxException(payload, "Dropbox metadata lookup failed.");
        }

        return ParseItem(payload);
    }

    public async Task<IReadOnlyList<DropboxItem>> EnumerateAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var response = await SendApiRequestAsync(
            cancellationToken => CreateApiRequestAsync(
                "/files/list_folder",
                new
                {
                    path = ToDropboxPath(relativePath),
                    recursive = false,
                    include_deleted = false,
                    include_mounted_folders = true,
                    include_non_downloadable_files = true,
                },
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var payload = response.Payload;
        if (response.StatusCode == HttpStatusCode.Conflict && payload.Contains("not_found", StringComparison.OrdinalIgnoreCase))
        {
            throw new DirectoryNotFoundException($"The Dropbox folder '{DropboxPath.BuildPath(relativePath)}' was not found.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw CreateDropboxException(payload, $"Dropbox folder listing failed ({(int)response.StatusCode} {response.ReasonPhrase}).");
        }

        var listResponse = JsonSerializer.Deserialize<ListFolderResponse>(payload, SerializerOptions)
            ?? throw new InvalidOperationException("Dropbox did not return a folder listing payload.");
        return listResponse.Entries.Select(entry => entry.ToDropboxItem()).ToArray();
    }

    public async Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        using var request = await CreateContentRequestAsync("/files/download", new { path = ToDropboxPath(relativePath) }, cancellationToken).ConfigureAwait(false);
        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            response.Dispose();
            throw CreateDropboxException(payload, "Opening a Dropbox file failed.");
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task UploadFileAsync(string relativePath, string localFilePath, bool overwrite = true, CancellationToken cancellationToken = default) =>
        UploadFileInternalAsync(relativePath, localFilePath, overwrite, clientModifiedUtc: null, cancellationToken);

    public async Task CreateDirectoryAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var response = await SendApiRequestAsync(
            cancellationToken => CreateApiRequestAsync("/files/create_folder_v2", new { path = ToDropboxPath(relativePath), autorename = false }, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var payload = response.Payload;
        if (!response.IsSuccessStatusCode && !payload.Contains("conflict", StringComparison.OrdinalIgnoreCase))
        {
            throw CreateDropboxException(payload, "Creating a Dropbox folder failed.");
        }
    }

    public Task DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default) =>
        DeletePathAsync(relativePath, cancellationToken);

    public Task DeleteDirectoryAsync(string relativePath, CancellationToken cancellationToken = default) =>
        DeletePathAsync(relativePath, cancellationToken);

    public async Task MoveAsync(string sourceRelativePath, string destinationRelativePath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        if (overwrite)
        {
            var destinationEntry = await GetEntryAsync(destinationRelativePath, cancellationToken).ConfigureAwait(false);
            if (destinationEntry.Exists)
            {
                await DeletePathAsync(destinationRelativePath, cancellationToken).ConfigureAwait(false);
            }
        }

        var response = await SendApiRequestAsync(
            cancellationToken => CreateApiRequestAsync(
                "/files/move_v2",
                new { from_path = ToDropboxPath(sourceRelativePath), to_path = ToDropboxPath(destinationRelativePath), autorename = false },
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var payload = response.Payload;
        if (!response.IsSuccessStatusCode)
        {
            throw CreateDropboxException(payload, "Moving a Dropbox item failed.");
        }
    }

    public async Task SetLastWriteTimeUtcAsync(string relativePath, DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default)
    {
        var temporaryFilePath = Path.Combine(Path.GetTempPath(), "UsbFileSync", "DropboxTimestamps", $"{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryFilePath)!);

        try
        {
            await using (var sourceStream = await OpenReadAsync(relativePath, cancellationToken).ConfigureAwait(false))
            await using (var destinationStream = File.Create(temporaryFilePath))
            {
                await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
            }

            await UploadFileInternalAsync(relativePath, temporaryFilePath, overwrite: true, lastWriteTimeUtc.ToUniversalTime(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporaryFilePath))
            {
                File.Delete(temporaryFilePath);
            }
        }
    }

    private async Task UploadFileInternalAsync(string relativePath, string localFilePath, bool overwrite, DateTime? clientModifiedUtc, CancellationToken cancellationToken)
    {
        var dropboxArg = new Dictionary<string, object?>
        {
            ["path"] = ToDropboxPath(relativePath),
            ["mode"] = overwrite ? "overwrite" : "add",
            ["autorename"] = false,
            ["mute"] = true,
        };

        if (clientModifiedUtc.HasValue)
        {
            dropboxArg["client_modified"] = clientModifiedUtc.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        var response = await SendApiRequestAsync(
            async cancellationToken =>
            {
                var request = await CreateContentRequestAsync("/files/upload", dropboxArg, cancellationToken).ConfigureAwait(false);
                var fileStream = File.OpenRead(localFilePath);
                request.Content = new StreamContent(fileStream);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return request;
            },
            cancellationToken).ConfigureAwait(false);
        var payload = response.Payload;
        if (!response.IsSuccessStatusCode)
        {
            throw CreateDropboxException(payload, "Uploading a Dropbox file failed.");
        }
    }

    private async Task DeletePathAsync(string relativePath, CancellationToken cancellationToken)
    {
        var response = await SendApiRequestAsync(
            cancellationToken => CreateApiRequestAsync("/files/delete_v2", new { path = ToDropboxPath(relativePath) }, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var payload = response.Payload;
        if (!response.IsSuccessStatusCode && !payload.Contains("not_found", StringComparison.OrdinalIgnoreCase))
        {
            throw CreateDropboxException(payload, "Deleting a Dropbox item failed.");
        }
    }

    private async Task<DropboxApiResponse> SendApiRequestAsync(
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        CancellationToken cancellationToken)
    {
        using var request = await requestFactory(cancellationToken).ConfigureAwait(false);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (IsMissingScopeError(response.StatusCode, payload))
        {
            _authenticationService.InvalidateCachedToken();
            var realError = CreateDropboxException(payload, "Dropbox API rejected the request.").Message;
            throw new InvalidOperationException(
                $"{realError}\n\nUsbFileSync cleared the cached Dropbox sign-in. "
                + "Confirm the Dropbox app Permissions tab has files.metadata.read, files.content.read, files.metadata.write, and files.content.write enabled and submitted, then retry.");
        }

        return new DropboxApiResponse(response.StatusCode, response.ReasonPhrase, payload);
    }

    private async Task<HttpRequestMessage> CreateApiRequestAsync(string relativeUrl, object payload, CancellationToken cancellationToken)
    {
        var accessToken = await _authenticationService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        return new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl + relativeUrl)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", accessToken) },
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json"),
        };
    }

    private async Task<HttpRequestMessage> CreateContentRequestAsync(string relativeUrl, object dropboxArg, CancellationToken cancellationToken)
    {
        var accessToken = await _authenticationService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var request = new HttpRequestMessage(HttpMethod.Post, ContentBaseUrl + relativeUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(dropboxArg, SerializerOptions));
        return request;
    }

    private static DropboxItem ParseItem(string payload)
    {
        var entry = JsonSerializer.Deserialize<DropboxEntry>(payload, SerializerOptions)
            ?? throw new InvalidOperationException("Dropbox did not return a metadata payload.");
        return entry.ToDropboxItem();
    }

    private static bool IsMissingScopeError(HttpStatusCode statusCode, string payload)
    {
        if (statusCode != HttpStatusCode.BadRequest && statusCode != HttpStatusCode.Forbidden)
        {
            return false;
        }

        return payload.Contains("missing_scope", StringComparison.OrdinalIgnoreCase)
            || payload.Contains("required scope", StringComparison.OrdinalIgnoreCase)
            || payload.Contains("files.metadata.read", StringComparison.OrdinalIgnoreCase);
    }

    private static Exception CreateDropboxException(string payload, string fallbackMessage)
    {
        try
        {
            var error = JsonSerializer.Deserialize<DropboxErrorEnvelope>(payload, SerializerOptions);
            var userMessage = error?.UserMessage?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(userMessage))
            {
                return new InvalidOperationException(userMessage);
            }

            var summary = error?.ErrorSummary?.Trim();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                return new InvalidOperationException(summary);
            }

            if (error?.Error.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                var compactError = error.Error.ToString();
                if (!string.IsNullOrWhiteSpace(compactError))
                {
                    return new InvalidOperationException($"{fallbackMessage} {compactError}".Trim());
                }
            }
        }
        catch (JsonException)
        {
        }

        if (!string.IsNullOrWhiteSpace(payload))
        {
            var compactPayload = payload.Length > 400 ? payload[..400] + "..." : payload;
            return new InvalidOperationException($"{fallbackMessage} {compactPayload}".Trim());
        }

        return new InvalidOperationException(fallbackMessage);
    }

    private static string NormalizeRelativePath(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Replace('\\', '/').Trim('/');

    private static string ToDropboxPath(string? relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        return string.IsNullOrEmpty(normalizedRelativePath) ? string.Empty : "/" + normalizedRelativePath;
    }

    internal sealed record DropboxItem(
        string Id,
        string Name,
        bool IsDirectory,
        long? Size,
        DateTime? LastWriteTimeUtc,
        bool Exists)
    {
        public static DropboxItem Root { get; } = new("root", "Dropbox", true, null, null, true);

        public static DropboxItem NotFound(string relativePath) =>
            new(relativePath, Path.GetFileName(relativePath.Replace('/', Path.DirectorySeparatorChar)), false, null, null, false);
    }

    private sealed record ListFolderResponse([property: JsonPropertyName("entries")] DropboxEntry[] Entries);

    private sealed record DropboxErrorEnvelope(
        [property: JsonPropertyName("error_summary")] string? ErrorSummary,
        [property: JsonPropertyName("error")] JsonElement Error,
        [property: JsonPropertyName("user_message")] DropboxUserMessage? UserMessage);

    private sealed record DropboxUserMessage([property: JsonPropertyName("text")] string? Text);

    private sealed record DropboxEntry(
        [property: JsonPropertyName(".tag")] string Tag,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("client_modified")] DateTime? ClientModified,
        [property: JsonPropertyName("server_modified")] DateTime? ServerModified)
    {
        public DropboxItem ToDropboxItem() =>
            new(
                Id ?? Name,
                Name,
                string.Equals(Tag, "folder", StringComparison.OrdinalIgnoreCase),
                Size,
                ClientModified ?? ServerModified,
                true);
    }

    private sealed record DropboxApiResponse(HttpStatusCode StatusCode, string? ReasonPhrase, string Payload)
    {
        public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
    }
}