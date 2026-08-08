using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Infrastructure.Google;

public sealed class GoogleOAuthAuthorizationService : IGoogleAuthorizationService
{
    private const string AllowedTokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
    private static readonly string[] RequiredScopes =
    [
        "openid",
        "email",
        "profile",
        "https://www.googleapis.com/auth/calendar.events.readonly",
        "https://www.googleapis.com/auth/meetings.space.readonly"
    ];
    private static readonly string[] RequiredDataScopes =
    [
        "https://www.googleapis.com/auth/calendar.events.readonly",
        "https://www.googleapis.com/auth/meetings.space.readonly"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IGoogleTokenStore _tokenStore;
    private readonly HttpClient _httpClient;
    private readonly IGoogleOAuthUserConsent _userConsent;
    private readonly TimeProvider _timeProvider;

    public GoogleOAuthAuthorizationService(
        IGoogleTokenStore tokenStore,
        HttpClient httpClient,
        IGoogleOAuthUserConsent userConsent)
        : this(tokenStore, httpClient, userConsent, TimeProvider.System)
    {
    }

    public GoogleOAuthAuthorizationService(
        IGoogleTokenStore tokenStore,
        HttpClient httpClient,
        IGoogleOAuthUserConsent userConsent,
        TimeProvider timeProvider)
    {
        _tokenStore = tokenStore;
        _httpClient = httpClient;
        _userConsent = userConsent;
        _timeProvider = timeProvider;
    }

    public async Task<GoogleConnectionInfo> GetConnectionInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await _tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return token is null
            ? new GoogleConnectionInfo(false, null, null)
            : new GoogleConnectionInfo(true, token.AccountEmail, token.AccountUserId);
    }

    public async Task<GoogleConnectionInfo> ConnectAsync(
        string clientConfigurationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientConfigurationPath);
        var json = await File.ReadAllTextAsync(clientConfigurationPath, cancellationToken).ConfigureAwait(false);
        var configuration = ParseConfiguration(json);

        var state = CreateBase64UrlSecret(32);
        var codeVerifier = CreateBase64UrlSecret(64);
        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var authorizationCode = await _userConsent.RequestCodeAsync(
            redirectUri => BuildAuthorizationUri(configuration, redirectUri, state, codeChallenge),
            state,
            cancellationToken).ConfigureAwait(false);

        var tokenResponse = await ExchangeCodeAsync(
            configuration,
            authorizationCode,
            codeVerifier,
            cancellationToken).ConfigureAwait(false);
        var identity = await GetIdentityAsync(tokenResponse.AccessToken, cancellationToken).ConfigureAwait(false);
        var grantedScopes = ParseScopes(tokenResponse.Scope);
        EnsureRequiredScopes(grantedScopes);

        var token = new GoogleOAuthToken
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresAtUtc = _timeProvider.GetUtcNow().AddSeconds(tokenResponse.ExpiresIn),
            AccountEmail = identity.Email,
            AccountUserId = "users/" + identity.Subject,
            ClientId = configuration.ClientId,
            ClientSecret = configuration.ClientSecret,
            TokenEndpoint = configuration.TokenEndpoint.AbsoluteUri,
            GrantedScopes = grantedScopes
        };
        await _tokenStore.SaveAsync(token, cancellationToken).ConfigureAwait(false);
        return new GoogleConnectionInfo(true, token.AccountEmail, token.AccountUserId);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _tokenStore.DeleteAsync(cancellationToken);

    private async Task<TokenExchangeResponse> ExchangeCodeAsync(
        OAuthClientConfiguration configuration,
        GoogleOAuthAuthorizationCode authorizationCode,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("client_id", configuration.ClientId),
            new("code", authorizationCode.Code),
            new("code_verifier", codeVerifier),
            new("grant_type", "authorization_code"),
            new("redirect_uri", authorizationCode.RedirectUri.AbsoluteUri)
        };
        if (!string.IsNullOrWhiteSpace(configuration.ClientSecret))
            fields.Add(new("client_secret", configuration.ClientSecret));

        using var request = new HttpRequestMessage(HttpMethod.Post, configuration.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new GoogleAuthenticationRequiredException("Google odrzucił kod autoryzacyjny OAuth.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<TokenExchangeResponse>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result?.AccessToken)
            || string.IsNullOrWhiteSpace(result.RefreshToken)
            || result.ExpiresIn <= 0)
        {
            throw new InvalidDataException("Google zwrócił niepełny token OAuth.");
        }

        return result;
    }

    private async Task<GoogleIdentity> GetIdentityAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var identity = await JsonSerializer.DeserializeAsync<GoogleIdentity>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(identity?.Subject) || string.IsNullOrWhiteSpace(identity.Email))
            throw new InvalidDataException("Google nie zwrócił identyfikatora i adresu konta.");

        return identity;
    }

    private static OAuthClientConfiguration ParseConfiguration(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("installed", out var installed))
                throw new InvalidDataException("Plik nie zawiera konfiguracji OAuth typu Desktop app.");

            var clientId = GetRequiredString(installed, "client_id");
            var authEndpoint = ParseHttpsUri(GetRequiredString(installed, "auth_uri"), "auth_uri");
            var tokenEndpoint = ParseHttpsUri(GetRequiredString(installed, "token_uri"), "token_uri");
            var clientSecret = installed.TryGetProperty("client_secret", out var secretElement)
                ? secretElement.GetString()
                : null;

            if (!string.Equals(authEndpoint.Host, "accounts.google.com", StringComparison.OrdinalIgnoreCase)
                || authEndpoint.AbsolutePath is not ("/o/oauth2/auth" or "/o/oauth2/v2/auth"))
            {
                throw new InvalidDataException("Konfiguracja zawiera nieobsługiwany endpoint autoryzacji Google.");
            }

            if (!string.Equals(tokenEndpoint.AbsoluteUri, AllowedTokenEndpoint, StringComparison.Ordinal))
                throw new InvalidDataException("Konfiguracja zawiera nieobsługiwany endpoint tokenu Google.");

            return new OAuthClientConfiguration(clientId, clientSecret, authEndpoint, tokenEndpoint);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Plik konfiguracji OAuth Google nie jest prawidłowym JSON-em.", ex);
        }
    }

    private static Uri BuildAuthorizationUri(
        OAuthClientConfiguration configuration,
        Uri redirectUri,
        string state,
        string codeChallenge)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", configuration.ClientId),
            new("redirect_uri", redirectUri.AbsoluteUri),
            new("scope", string.Join(' ', RequiredScopes)),
            new("state", state),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", "S256"),
            new("access_type", "offline"),
            new("prompt", "consent"),
            new("include_granted_scopes", "true")
        };
        var query = string.Join(
            "&",
            parameters.Select(pair =>
                Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        var builder = new UriBuilder(configuration.AuthorizationEndpoint) { Query = query };
        return builder.Uri;
    }

    private static IReadOnlyList<string> ParseScopes(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void EnsureRequiredScopes(IReadOnlyList<string> grantedScopes)
    {
        var granted = grantedScopes.ToHashSet(StringComparer.Ordinal);
        if (RequiredDataScopes.Any(scope => !granted.Contains(scope)))
            throw new GoogleAuthenticationRequiredException("Nie przyznano wszystkich wymaganych uprawnień Google.");
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"Konfiguracja OAuth nie zawiera pola {propertyName}.");
        }

        return property.GetString()!;
    }

    private static Uri ParseHttpsUri(string value, string fieldName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Pole {fieldName} nie zawiera bezpiecznego adresu HTTPS.");
        }

        return uri;
    }

    private static string CreateBase64UrlSecret(int byteCount)
        => Base64UrlEncode(RandomNumberGenerator.GetBytes(byteCount));

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record OAuthClientConfiguration(
        string ClientId,
        string? ClientSecret,
        Uri AuthorizationEndpoint,
        Uri TokenEndpoint);

    private sealed class TokenExchangeResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        public string? Scope { get; init; }
    }

    private sealed class GoogleIdentity
    {
        [JsonPropertyName("sub")]
        public string Subject { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;
    }
}
