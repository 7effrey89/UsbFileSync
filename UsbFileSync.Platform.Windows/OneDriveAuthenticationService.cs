using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsbFileSync.Platform.Windows;

internal sealed class OneDriveAuthenticationService
{
    private const string Scope = "Files.ReadWrite offline_access User.Read";
    private const string TokenCacheScopeKey = "graph-files-rw";
    private static readonly HttpClient HttpClient = new();
    private static readonly TimeSpan AccessTokenRefreshSkew = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _clientId;
    private readonly string _requestedTenantId;
    private readonly string _tokenCachePrefix;
    private readonly OneDriveTokenStore _tokenStore;
    private readonly Func<Uri, bool> _openBrowser;

    public OneDriveAuthenticationService(
        string clientId,
        string tenantId,
        string? cacheKeyPrefix = null,
        OneDriveTokenStore? tokenStore = null,
        Func<Uri, bool>? openBrowser = null)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("A OneDrive application client ID is required.", nameof(clientId));
        }

        _clientId = clientId.Trim();
        _requestedTenantId = string.IsNullOrWhiteSpace(tenantId) ? "common" : tenantId.Trim();
        _tokenCachePrefix = string.IsNullOrWhiteSpace(cacheKeyPrefix) ? _clientId : cacheKeyPrefix.Trim();
        _tokenStore = tokenStore ?? new OneDriveTokenStore();
        _openBrowser = openBrowser ?? OpenBrowser;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        foreach (var authorityTenantId in GetAuthorityTenantIds())
        {
            var cacheKey = BuildCacheKey(authorityTenantId);
            var cachedToken = _tokenStore.Load(cacheKey);
            if (cachedToken is not null && cachedToken.ExpiresAtUtc > DateTime.UtcNow.Add(AccessTokenRefreshSkew))
            {
                return cachedToken.AccessToken;
            }
        }

        Exception? lastException = null;
        foreach (var authorityTenantId in GetAuthorityTenantIds())
        {
            var cacheKey = BuildCacheKey(authorityTenantId);
            var token = _tokenStore.Load(cacheKey);
            if (token is not null && !string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                try
                {
                    var refreshedToken = await RefreshTokenAsync(token.RefreshToken, authorityTenantId, cancellationToken).ConfigureAwait(false);
                    _tokenStore.Save(cacheKey, refreshedToken);
                    return refreshedToken.AccessToken;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    _tokenStore.Delete(cacheKey);
                    lastException = exception;
                    if (!ShouldTryConsumersFallback(authorityTenantId, exception))
                    {
                        break;
                    }

                    continue;
                }
            }

            try
            {
                var authorizedToken = await AuthorizeInteractivelyAsync(authorityTenantId, cancellationToken).ConfigureAwait(false);
                _tokenStore.Save(cacheKey, authorizedToken);
                return authorizedToken.AccessToken;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = exception;
                if (!ShouldTryConsumersFallback(authorityTenantId, exception))
                {
                    break;
                }
            }
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        throw new InvalidOperationException("OneDrive sign-in could not be completed.");
    }

    private async Task<OneDriveAuthToken> AuthorizeInteractivelyAsync(string authorityTenantId, CancellationToken cancellationToken)
    {
        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var listenerPrefix = BuildLoopbackRedirectUri();

        using var listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        listener.Start();

        var authorizationUrl = BuildAuthorizationUri(authorityTenantId, listenerPrefix, challenge, state);
        if (!_openBrowser(authorizationUrl))
        {
            throw new InvalidOperationException("OneDrive sign-in could not be started in the system browser.");
        }

        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(5));

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(timeoutCancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("OneDrive sign-in timed out before the authorization response was received.");
        }

        var request = context.Request;
        var response = context.Response;
        try
        {
            var returnedState = request.QueryString["state"];
            if (!string.Equals(state, returnedState, StringComparison.Ordinal))
            {
                await WriteBrowserResponseAsync(response, success: false, "OneDrive sign-in was rejected because the OAuth state did not match.").ConfigureAwait(false);
                throw new InvalidOperationException("OneDrive sign-in failed because the OAuth state did not match.");
            }

            var error = request.QueryString["error"];
            var errorDescription = request.QueryString["error_description"];
            if (!string.IsNullOrWhiteSpace(error))
            {
                var message = string.IsNullOrWhiteSpace(errorDescription)
                    ? $"OneDrive sign-in failed: {error}."
                    : $"OneDrive sign-in failed: {errorDescription}.";
                await WriteBrowserResponseAsync(response, success: false, message).ConfigureAwait(false);
                throw new InvalidOperationException(message);
            }

            var code = request.QueryString["code"];
            if (string.IsNullOrWhiteSpace(code))
            {
                await WriteBrowserResponseAsync(response, success: false, "OneDrive sign-in did not return an authorization code.").ConfigureAwait(false);
                throw new InvalidOperationException("OneDrive sign-in did not return an authorization code.");
            }

            try
            {
                var token = await ExchangeAuthorizationCodeAsync(code, authorityTenantId, listenerPrefix.TrimEnd('/'), verifier, cancellationToken).ConfigureAwait(false);
                await WriteBrowserResponseAsync(response, success: true, "OneDrive is now connected. You can close this browser window and return to UsbFileSync.").ConfigureAwait(false);
                return token;
            }
            catch (Exception exception)
            {
                await WriteBrowserResponseAsync(response, success: false, exception.Message).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            response.Close();
        }
    }

    private async Task<OneDriveAuthToken> ExchangeAuthorizationCodeAsync(
        string code,
        string authorityTenantId,
        string redirectUri,
        string verifier,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildTokenEndpoint(authorityTenantId))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
                ["scope"] = Scope,
            })
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateTokenException(payload, "OneDrive authorization code exchange failed.");
        }

        return ParseTokenResponse(payload, existingRefreshToken: null);
    }

    private async Task<OneDriveAuthToken> RefreshTokenAsync(string refreshToken, string authorityTenantId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildTokenEndpoint(authorityTenantId))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["scope"] = Scope,
            })
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateTokenException(payload, "OneDrive access token refresh failed.");
        }

        return ParseTokenResponse(payload, refreshToken);
    }

    private Uri BuildAuthorizationUri(string authorityTenantId, string listenerPrefix, string challenge, string state)
    {
        var redirectUri = listenerPrefix.TrimEnd('/');
        var queryParameters = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query",
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["state"] = state,
        };

        var queryString = string.Join("&", queryParameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{BuildAuthorizationEndpoint(authorityTenantId)}?{queryString}");
    }

    internal static bool ShouldTryConsumersFallback(string authorityTenantId, Exception exception)
    {
        if (!string.Equals(authorityTenantId, "common", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var message = exception.Message;
        return message.Contains("userAudience", StringComparison.OrdinalIgnoreCase)
            && message.Contains("/common/", StringComparison.OrdinalIgnoreCase)
            && message.Contains("Consumer", StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetAuthorityTenantIds()
    {
        yield return _requestedTenantId;
    }

    private string BuildAuthorizationEndpoint(string authorityTenantId) =>
        $"https://login.microsoftonline.com/{authorityTenantId}/oauth2/v2.0/authorize";

    private string BuildTokenEndpoint(string authorityTenantId) =>
        $"https://login.microsoftonline.com/{authorityTenantId}/oauth2/v2.0/token";

    private string BuildCacheKey(string authorityTenantId) =>
        $"{_tokenCachePrefix}|{authorityTenantId}|{TokenCacheScopeKey}";

    private static OneDriveAuthToken ParseTokenResponse(string payload, string? existingRefreshToken)
    {
        try
        {
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(payload, SerializerOptions)
                ?? throw new InvalidOperationException("OneDrive sign-in did not return a token payload.");

            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                throw new InvalidOperationException("OneDrive sign-in did not return an access token.");
            }

            return new OneDriveAuthToken(
                tokenResponse.AccessToken,
                string.IsNullOrWhiteSpace(tokenResponse.RefreshToken) ? existingRefreshToken ?? string.Empty : tokenResponse.RefreshToken,
                DateTime.UtcNow.AddSeconds(Math.Max(1, tokenResponse.ExpiresIn)),
                tokenResponse.Scope ?? Scope);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("OneDrive returned a token response that could not be parsed.", exception);
        }
    }

    private static Exception CreateTokenException(string payload, string fallbackMessage)
    {
        try
        {
            var error = JsonSerializer.Deserialize<TokenErrorResponse>(payload, SerializerOptions);
            var description = error?.ErrorDescription?.Trim();
            if (!string.IsNullOrWhiteSpace(description))
            {
                return new InvalidOperationException(description);
            }

            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                return new InvalidOperationException($"{fallbackMessage} {error.Error}".Trim());
            }
        }
        catch (JsonException)
        {
        }

        return new InvalidOperationException(fallbackMessage);
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, bool success, string message)
    {
        response.StatusCode = success ? 200 : 400;
        response.ContentType = "text/html; charset=utf-8";
        var statusText = success ? "OneDrive connected" : "OneDrive sign-in failed";
        var htmlBuilder = new StringBuilder();
        htmlBuilder.AppendLine("<!doctype html>");
        htmlBuilder.AppendLine("<html lang=\"en\">");
        htmlBuilder.AppendLine("<head>");
        htmlBuilder.AppendLine("    <meta charset=\"utf-8\" />");
        htmlBuilder.AppendLine("    <title>UsbFileSync OneDrive Sign-in</title>");
        htmlBuilder.AppendLine("    <style>");
        htmlBuilder.AppendLine("        body { font-family: Segoe UI, Arial, sans-serif; margin: 2rem; color: #202124; }");
        htmlBuilder.AppendLine("        .status { font-size: 1.2rem; font-weight: 600; margin-bottom: 0.75rem; }");
        htmlBuilder.AppendLine("    </style>");
        htmlBuilder.AppendLine("</head>");
        htmlBuilder.AppendLine("<body>");
        htmlBuilder.Append("    <div class=\"status\">");
        htmlBuilder.Append(WebUtility.HtmlEncode(statusText));
        htmlBuilder.AppendLine("</div>");
        htmlBuilder.Append("    <div>");
        htmlBuilder.Append(WebUtility.HtmlEncode(message));
        htmlBuilder.AppendLine("</div>");
        htmlBuilder.AppendLine("</body>");
        htmlBuilder.AppendLine("</html>");
        var html = htmlBuilder.ToString();

        var buffer = Encoding.UTF8.GetBytes(html);
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        await response.OutputStream.FlushAsync().ConfigureAwait(false);
    }

    private static string BuildLoopbackRedirectUri()
    {
        var port = GetAvailablePort();
        return $"http://localhost:{port}/";
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string CreateCodeVerifier()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncode(randomBytes);
    }

    private static string CreateCodeChallenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool OpenBrowser(Uri authorizationUri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(authorizationUri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = string.Empty;
    }

    private sealed class TokenErrorResponse
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;

        [JsonPropertyName("error_description")]
        public string ErrorDescription { get; set; } = string.Empty;
    }
}