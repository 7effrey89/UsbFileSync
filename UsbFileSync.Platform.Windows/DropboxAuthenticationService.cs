using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsbFileSync.Platform.Windows;

internal sealed class DropboxAuthenticationService
{
    private const string AuthorizationEndpoint = "https://www.dropbox.com/oauth2/authorize";
    private const string TokenEndpoint = "https://api.dropboxapi.com/oauth2/token";
    private const string TokenCacheScopeKey = "dropbox-rw";
    internal const string RedirectUri = "http://127.0.0.1:53682/";
    private static readonly HttpClient HttpClient = new();
    private static readonly TimeSpan AccessTokenRefreshSkew = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tokenCacheKey;
    private readonly DropboxTokenStore _tokenStore;
    private readonly Func<Uri, bool> _openBrowser;

    public DropboxAuthenticationService(
        string clientId,
        string? clientSecret = null,
        string? tokenCacheKey = null,
        DropboxTokenStore? tokenStore = null,
        Func<Uri, bool>? openBrowser = null)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("A Dropbox app key is required.", nameof(clientId));
        }

        _clientId = clientId.Trim();
        _clientSecret = clientSecret?.Trim() ?? string.Empty;
        _tokenCacheKey = string.IsNullOrWhiteSpace(tokenCacheKey)
            ? $"{_clientId}|{TokenCacheScopeKey}"
            : $"{tokenCacheKey.Trim()}|{TokenCacheScopeKey}";
        _tokenStore = tokenStore ?? new DropboxTokenStore();
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

    internal void InvalidateCachedToken()
    {
        _tokenStore.Delete(_tokenCacheKey);
    }

    private async Task<DropboxAuthToken> AuthorizeInteractivelyAsync(CancellationToken cancellationToken)
    {
        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var listenerPrefix = BuildLoopbackRedirectUri();

        using var listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException exception)
        {
            throw new InvalidOperationException($"Dropbox sign-in could not start the local callback listener for '{listenerPrefix}'. Make sure your Dropbox app allows this exact redirect URI and that the address is available on this machine. {exception.Message}", exception);
        }

        var authorizationUrl = BuildAuthorizationUri(listenerPrefix, challenge, state);
        if (!_openBrowser(authorizationUrl))
        {
            throw new InvalidOperationException("Dropbox sign-in could not be started in the system browser.");
        }

        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(5));

        HttpListenerContext context;
        while (true)
        {
            try
            {
                context = await listener.GetContextAsync().WaitAsync(timeoutCancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Dropbox sign-in timed out before the authorization response was received.");
            }

            var candidateRequest = context.Request;
            if (!ShouldIgnoreCallbackRequest(candidateRequest.QueryString["state"], candidateRequest.QueryString["code"], candidateRequest.QueryString["error"]))
            {
                break;
            }

            IgnoreRequest(context.Response);
        }

        var request = context.Request;
        var response = context.Response;
        try
        {
            var returnedState = request.QueryString["state"];
            if (!string.Equals(state, returnedState, StringComparison.Ordinal))
            {
                await WriteBrowserResponseAsync(response, success: false, "Dropbox sign-in was rejected because the OAuth state did not match.").ConfigureAwait(false);
                throw new InvalidOperationException("Dropbox sign-in failed because the OAuth state did not match.");
            }

            var error = request.QueryString["error"];
            var errorDescription = request.QueryString["error_description"];
            if (!string.IsNullOrWhiteSpace(error))
            {
                var message = string.IsNullOrWhiteSpace(errorDescription)
                    ? $"Dropbox sign-in failed: {error}."
                    : $"Dropbox sign-in failed: {errorDescription}.";
                await WriteBrowserResponseAsync(response, success: false, message).ConfigureAwait(false);
                throw new InvalidOperationException(message);
            }

            var code = request.QueryString["code"];
            if (string.IsNullOrWhiteSpace(code))
            {
                await WriteBrowserResponseAsync(response, success: false, "Dropbox sign-in did not return an authorization code.").ConfigureAwait(false);
                throw new InvalidOperationException("Dropbox sign-in did not return an authorization code.");
            }

            try
            {
                var token = await ExchangeAuthorizationCodeAsync(code, listenerPrefix, verifier, cancellationToken).ConfigureAwait(false);
                await WriteBrowserResponseAsync(response, success: true, "Dropbox is now connected. You can close this browser window and return to UsbFileSync.").ConfigureAwait(false);
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

    private async Task<DropboxAuthToken> ExchangeAuthorizationCodeAsync(string code, string redirectUri, string verifier, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        };

        if (!string.IsNullOrWhiteSpace(_clientSecret))
        {
            values["client_secret"] = _clientSecret;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values)
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateTokenException(payload, "Dropbox authorization code exchange failed.");
        }

        return ParseTokenResponse(payload, existingRefreshToken: null);
    }

    private async Task<DropboxAuthToken> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };

        if (!string.IsNullOrWhiteSpace(_clientSecret))
        {
            values["client_secret"] = _clientSecret;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(values)
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateTokenException(payload, "Dropbox access token refresh failed.");
        }

        return ParseTokenResponse(payload, refreshToken);
    }

    private Uri BuildAuthorizationUri(string listenerPrefix, string challenge, string state)
    {
        var queryParameters = new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["redirect_uri"] = listenerPrefix,
            ["response_type"] = "code",
            ["state"] = state,
            ["token_access_type"] = "offline",
        };

        var queryString = string.Join("&", queryParameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{AuthorizationEndpoint}?{queryString}");
    }

    private static DropboxAuthToken ParseTokenResponse(string payload, string? existingRefreshToken)
    {
        try
        {
            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(payload, SerializerOptions)
                ?? throw new InvalidOperationException("Dropbox sign-in did not return a token payload.");

            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                throw new InvalidOperationException("Dropbox sign-in did not return an access token.");
            }

            return new DropboxAuthToken(
                tokenResponse.AccessToken,
                string.IsNullOrWhiteSpace(tokenResponse.RefreshToken) ? existingRefreshToken ?? string.Empty : tokenResponse.RefreshToken,
                DateTime.UtcNow.AddSeconds(Math.Max(1, tokenResponse.ExpiresIn)),
                tokenResponse.Scope ?? string.Empty);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Dropbox returned a token response that could not be parsed.", exception);
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

        var title = success ? "Dropbox Connected" : "Dropbox Sign-In Failed";
        var bodyColor = success ? "#1b5e20" : "#b71c1c";
        var encodedTitle = WebUtility.HtmlEncode(title);
        var encodedMessage = WebUtility.HtmlEncode(message);
        var html = "<!doctype html>"
            + "<html><head><meta charset=\"utf-8\"><title>" + encodedTitle + "</title></head>"
            + "<body style=\"font-family:Segoe UI,Arial,sans-serif;margin:2rem;\">"
            + "<h1 style=\"color:" + bodyColor + ";\">" + encodedTitle + "</h1>"
            + "<p>" + encodedMessage + "</p>"
            + "</body></html>";
        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
    }

    private static string CreateCodeVerifier() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string CreateCodeChallenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string BuildLoopbackRedirectUri()
    {
        return RedirectUri;
    }

    internal static bool ShouldIgnoreCallbackRequest(string? returnedState, string? code, string? error)
    {
        return string.IsNullOrWhiteSpace(returnedState)
            && string.IsNullOrWhiteSpace(code)
            && string.IsNullOrWhiteSpace(error);
    }

    private static void IgnoreRequest(HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.NoContent;
        response.Close();
    }

    private static bool OpenBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
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

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);

    private sealed record TokenErrorResponse(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}