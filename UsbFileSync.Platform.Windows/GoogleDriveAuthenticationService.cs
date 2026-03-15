using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsbFileSync.Platform.Windows;

internal sealed class GoogleDriveAuthenticationService
{
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string Scope = "https://www.googleapis.com/auth/drive";
    private const string TokenCacheScopeKey = "drive-rw";
    private static readonly HttpClient HttpClient = new();
    private static readonly TimeSpan AccessTokenRefreshSkew = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tokenCacheKey;
    private readonly GoogleDriveTokenStore _tokenStore;
    private readonly Func<Uri, bool> _openBrowser;

    public GoogleDriveAuthenticationService(
        string clientId,
        string? clientSecret = null,
        GoogleDriveTokenStore? tokenStore = null,
        Func<Uri, bool>? openBrowser = null)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("A Google Drive OAuth client ID is required.", nameof(clientId));
        }

        _clientId = clientId;
        _clientSecret = clientSecret?.Trim() ?? string.Empty;
        _tokenCacheKey = $"{_clientId}|{TokenCacheScopeKey}";
        _tokenStore = tokenStore ?? new GoogleDriveTokenStore();
        _openBrowser = openBrowser ?? OpenBrowser;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = _tokenStore.Load(_tokenCacheKey);
        if (token is not null && token.ExpiresAtUtc > DateTime.UtcNow.Add(AccessTokenRefreshSkew))
        {
            return token.AccessToken;
        }

        if (token is not null && !string.IsNullOrWhiteSpace(token.RefreshToken))
        {
            try
            {
                var refreshedToken = await RefreshTokenAsync(token.RefreshToken, cancellationToken).ConfigureAwait(false);
                _tokenStore.Save(_tokenCacheKey, refreshedToken);
                return refreshedToken.AccessToken;
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
                _tokenStore.Delete(_tokenCacheKey);
            }
        }

        var authorizedToken = await AuthorizeInteractivelyAsync(cancellationToken).ConfigureAwait(false);
        _tokenStore.Save(_tokenCacheKey, authorizedToken);
        return authorizedToken.AccessToken;
    }

    private async Task<GoogleDriveAuthToken> AuthorizeInteractivelyAsync(CancellationToken cancellationToken)
    {
        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var listenerPrefix = BuildLoopbackRedirectUri();

        using var listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        listener.Start();

        var authorizationUrl = BuildAuthorizationUri(listenerPrefix, challenge, state);
        if (!_openBrowser(authorizationUrl))
        {
            throw new InvalidOperationException("Google Drive sign-in could not be started in the system browser.");
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
            throw new TimeoutException("Google Drive sign-in timed out before the authorization response was received.");
        }

        var request = context.Request;
        var response = context.Response;
        try
        {
            var returnedState = request.QueryString["state"];
            if (!string.Equals(state, returnedState, StringComparison.Ordinal))
            {
                await WriteBrowserResponseAsync(response, success: false, "Google Drive sign-in was rejected because the OAuth state did not match.").ConfigureAwait(false);
                throw new InvalidOperationException("Google Drive sign-in failed because the OAuth state did not match.");
            }

            var error = request.QueryString["error"];
            if (!string.IsNullOrWhiteSpace(error))
            {
                await WriteBrowserResponseAsync(response, success: false, $"Google Drive sign-in failed: {error}.").ConfigureAwait(false);
                throw new InvalidOperationException($"Google Drive sign-in failed: {error}.");
            }

            var code = request.QueryString["code"];
            if (string.IsNullOrWhiteSpace(code))
            {
                await WriteBrowserResponseAsync(response, success: false, "Google Drive sign-in did not return an authorization code.").ConfigureAwait(false);
                throw new InvalidOperationException("Google Drive sign-in did not return an authorization code.");
            }

            try
            {
                var token = await ExchangeAuthorizationCodeAsync(code, listenerPrefix.TrimEnd('/'), verifier, cancellationToken).ConfigureAwait(false);
                await WriteBrowserResponseAsync(response, success: true, "Google Drive is now connected. You can close this browser window and return to UsbFileSync.").ConfigureAwait(false);
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

    private async Task<GoogleDriveAuthToken> ExchangeAuthorizationCodeAsync(
        string code,
        string redirectUri,
        string verifier,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = _clientId,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        });

        if (!string.IsNullOrWhiteSpace(_clientSecret))
        {
            content.Headers.ContentType!.CharSet = "utf-8";
        }

        if (!string.IsNullOrWhiteSpace(_clientSecret))
        {
            var values = new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["code_verifier"] = verifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
            };

            content.Dispose();
            using var contentWithSecret = new FormUrlEncodedContent(values);
            using var requestWithSecret = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = contentWithSecret,
            };

            using var responseWithSecret = await HttpClient.SendAsync(requestWithSecret, cancellationToken).ConfigureAwait(false);
            var payloadWithSecret = await responseWithSecret.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!responseWithSecret.IsSuccessStatusCode)
            {
                throw CreateTokenException(payloadWithSecret, "Google Drive authorization code exchange failed.");
            }

            return ParseTokenResponse(payloadWithSecret, existingRefreshToken: null);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = content,
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateTokenException(payload, "Google Drive authorization code exchange failed.");
        }

        return ParseTokenResponse(payload, existingRefreshToken: null);
    }

    private async Task<GoogleDriveAuthToken> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });

        if (!string.IsNullOrWhiteSpace(_clientSecret))
        {
            content.Dispose();
            using var contentWithSecret = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            });

            using var requestWithSecret = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = contentWithSecret,
            };

            using var responseWithSecret = await HttpClient.SendAsync(requestWithSecret, cancellationToken).ConfigureAwait(false);
            var payloadWithSecret = await responseWithSecret.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!responseWithSecret.IsSuccessStatusCode)
            {
                throw CreateTokenException(payloadWithSecret, "Google Drive access token refresh failed.");
            }

            return ParseTokenResponse(payloadWithSecret, refreshToken);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = content,
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateTokenException(payload, "Google Drive access token refresh failed.");
        }

        return ParseTokenResponse(payload, refreshToken);
    }

    private Uri BuildAuthorizationUri(string listenerPrefix, string challenge, string state)
    {
        var redirectUri = listenerPrefix.TrimEnd('/');
        var queryParameters = new Dictionary<string, string>
        {
            ["access_type"] = "offline",
            ["client_id"] = _clientId,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = "consent",
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["state"] = state,
        };

        var queryString = string.Join("&", queryParameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{AuthorizationEndpoint}?{queryString}");
    }

    private static GoogleDriveAuthToken ParseTokenResponse(string payload, string? existingRefreshToken)
    {
        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(payload, SerializerOptions)
            ?? throw new InvalidOperationException("Google Drive returned an empty OAuth token response.");

        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("Google Drive did not return an access token.");
        }

        var refreshToken = string.IsNullOrWhiteSpace(tokenResponse.RefreshToken)
            ? existingRefreshToken ?? string.Empty
            : tokenResponse.RefreshToken;
        return new GoogleDriveAuthToken(
            tokenResponse.AccessToken,
            refreshToken,
            DateTime.UtcNow.AddSeconds(Math.Max(0, tokenResponse.ExpiresIn)),
            tokenResponse.Scope ?? Scope);
    }

    private static Exception CreateTokenException(string payload, string fallbackMessage)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                var error = errorElement.GetString();
                var description = document.RootElement.TryGetProperty("error_description", out var descriptionElement)
                    ? descriptionElement.GetString()
                    : null;

                if (string.Equals(error, "invalid_client", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(description) &&
                    description.Contains("client_secret is missing", StringComparison.OrdinalIgnoreCase))
                {
                    return new InvalidOperationException(
                        "Google Drive authorization code exchange failed because this OAuth client expects a client secret. Enter the Google client secret in Application Settings, or create a Google OAuth client that can be used without a secret.");
                }

                return new InvalidOperationException(string.IsNullOrWhiteSpace(description)
                    ? $"{fallbackMessage} {error}".Trim()
                    : $"{fallbackMessage} {description}".Trim());
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
        var html = $"""
            <!doctype html>
            <html>
            <head>
                <meta charset="utf-8" />
                <title>UsbFileSync Google Drive</title>
            </head>
            <body style="font-family: Segoe UI, sans-serif; margin: 2rem;">
                <h2>{WebUtility.HtmlEncode(success ? "Google Drive connected" : "Google Drive connection failed")}</h2>
                <p>{WebUtility.HtmlEncode(message)}</p>
            </body>
            </html>
            """;
        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
    }

    private static string BuildLoopbackRedirectUri()
    {
        var port = GetAvailablePort();
        return $"http://127.0.0.1:{port}/";
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
}