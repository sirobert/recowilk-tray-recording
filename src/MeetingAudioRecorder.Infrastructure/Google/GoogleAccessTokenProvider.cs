using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingAudioRecorder.Infrastructure.Google;

public sealed class GoogleAccessTokenProvider : IGoogleAccessTokenProvider
{
    private const string AllowedTokenEndpoint = "https://oauth2.googleapis.com/token";
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IGoogleTokenStore _tokenStore;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    [ActivatorUtilitiesConstructor]
    public GoogleAccessTokenProvider(IGoogleTokenStore tokenStore, HttpClient httpClient)
        : this(tokenStore, httpClient, TimeProvider.System)
    {
    }

    public GoogleAccessTokenProvider(
        IGoogleTokenStore tokenStore,
        HttpClient httpClient,
        TimeProvider timeProvider)
    {
        _tokenStore = tokenStore;
        _httpClient = httpClient;
        _timeProvider = timeProvider;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = await LoadRequiredTokenAsync(cancellationToken).ConfigureAwait(false);
        if (!RequiresRefresh(token))
            return token.AccessToken;

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            token = await LoadRequiredTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!RequiresRefresh(token))
                return token.AccessToken;

            return await RefreshAsync(token, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<GoogleOAuthToken> LoadRequiredTokenAsync(CancellationToken cancellationToken)
        => await _tokenStore.LoadAsync(cancellationToken).ConfigureAwait(false)
           ?? throw new GoogleAuthenticationRequiredException(
               "Połącz konto Google w ustawieniach aplikacji.");

    private bool RequiresRefresh(GoogleOAuthToken token)
        => token.ExpiresAtUtc <= _timeProvider.GetUtcNow() + RefreshSkew;

    private async Task<string> RefreshAsync(
        GoogleOAuthToken token,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(token.TokenEndpoint, UriKind.Absolute, out var endpoint)
            || !string.Equals(endpoint.AbsoluteUri, AllowedTokenEndpoint, StringComparison.Ordinal))
        {
            throw new GoogleAuthenticationRequiredException(
                "Konfiguracja OAuth Google zawiera nieobsługiwany endpoint tokenu.");
        }

        var fields = new List<KeyValuePair<string, string>>
        {
            new("client_id", token.ClientId),
            new("refresh_token", token.RefreshToken),
            new("grant_type", "refresh_token")
        };
        if (!string.IsNullOrWhiteSpace(token.ClientSecret))
            fields.Add(new("client_secret", token.ClientSecret));

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            throw new GoogleAuthenticationRequiredException(
                "Sesja Google wygasła lub została cofnięta. Połącz konto ponownie.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var refreshed = await JsonSerializer.DeserializeAsync<TokenRefreshResponse>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(refreshed?.AccessToken) || refreshed.ExpiresIn <= 0)
            throw new InvalidDataException("Google zwrócił niepełną odpowiedź odświeżenia tokenu.");

        var updated = new GoogleOAuthToken
        {
            AccessToken = refreshed.AccessToken,
            RefreshToken = token.RefreshToken,
            ExpiresAtUtc = _timeProvider.GetUtcNow().AddSeconds(refreshed.ExpiresIn),
            AccountEmail = token.AccountEmail,
            AccountUserId = token.AccountUserId,
            ClientId = token.ClientId,
            ClientSecret = token.ClientSecret,
            TokenEndpoint = token.TokenEndpoint,
            GrantedScopes = token.GrantedScopes
        };
        await _tokenStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated.AccessToken;
    }

    private sealed class TokenRefreshResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
