using System.Net;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Infrastructure.Google;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace MeetingAudioRecorder.Audio.Tests;

public sealed class GoogleAccessTokenProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DependencyInjection_ResolvesAccessTokenProviderWithTypedHttpClient()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IGoogleTokenStore>());
        services.AddHttpClient<IGoogleAccessTokenProvider, GoogleAccessTokenProvider>();
        using var provider = services.BuildServiceProvider();

        var tokenProvider = provider.GetRequiredService<IGoogleAccessTokenProvider>();

        Assert.IsType<GoogleAccessTokenProvider>(tokenProvider);
    }

    [Fact]
    public async Task ValidStoredToken_IsReturnedWithoutNetworkCall()
    {
        var store = new Mock<IGoogleTokenStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateToken(Now.AddMinutes(10)));
        var handler = new RefreshHandler(_ => throw new InvalidOperationException("No request expected."));
        var provider = new GoogleAccessTokenProvider(store.Object, new HttpClient(handler), new FixedTimeProvider(Now));

        var accessToken = await provider.GetAccessTokenAsync();

        Assert.Equal("old-access-token", accessToken);
        Assert.Empty(handler.Requests);
        store.Verify(value => value.SaveAsync(It.IsAny<GoogleOAuthToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExpiredToken_IsRefreshedAndPersistedUsingPostBody()
    {
        var stored = CreateToken(Now.AddMinutes(-1));
        GoogleOAuthToken? saved = null;
        var store = new Mock<IGoogleTokenStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(stored);
        store.Setup(value => value.SaveAsync(It.IsAny<GoogleOAuthToken>(), It.IsAny<CancellationToken>()))
            .Callback<GoogleOAuthToken, CancellationToken>((token, _) => saved = token)
            .Returns(Task.CompletedTask);
        var handler = new RefreshHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"access_token\":\"new-access-token\",\"expires_in\":3600,\"token_type\":\"Bearer\"}",
                System.Text.Encoding.UTF8,
                "application/json")
        });
        var provider = new GoogleAccessTokenProvider(store.Object, new HttpClient(handler), new FixedTimeProvider(Now));

        var accessToken = await provider.GetAccessTokenAsync();

        Assert.Equal("new-access-token", accessToken);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://oauth2.googleapis.com/token", request.Uri?.AbsoluteUri);
        Assert.DoesNotContain("old-refresh-token", request.Uri?.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("refresh_token=old-refresh-token", request.Body, StringComparison.Ordinal);
        Assert.Contains("client_id=desktop-client-id", request.Body, StringComparison.Ordinal);
        Assert.NotNull(saved);
        Assert.Equal("new-access-token", saved.AccessToken);
        Assert.Equal("old-refresh-token", saved.RefreshToken);
        Assert.Equal(Now.AddHours(1), saved.ExpiresAtUtc);
        Assert.Equal(stored.AccountUserId, saved.AccountUserId);
    }

    [Fact]
    public async Task FailedRefresh_DoesNotOverwriteStoredToken()
    {
        var store = new Mock<IGoogleTokenStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateToken(Now.AddMinutes(-1)));
        var handler = new RefreshHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"invalid_grant\"}")
        });
        var provider = new GoogleAccessTokenProvider(store.Object, new HttpClient(handler), new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<GoogleAuthenticationRequiredException>(() => provider.GetAccessTokenAsync());

        store.Verify(value => value.SaveAsync(It.IsAny<GoogleOAuthToken>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingStoredToken_RequiresAuthentication()
    {
        var store = new Mock<IGoogleTokenStore>();
        store.Setup(value => value.LoadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoogleOAuthToken?)null);
        var provider = new GoogleAccessTokenProvider(
            store.Object,
            new HttpClient(new RefreshHandler(_ => throw new InvalidOperationException())),
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<GoogleAuthenticationRequiredException>(() => provider.GetAccessTokenAsync());
    }

    private static GoogleOAuthToken CreateToken(DateTimeOffset expiresAt)
        => new()
        {
            AccessToken = "old-access-token",
            RefreshToken = "old-refresh-token",
            ExpiresAtUtc = expiresAt,
            AccountEmail = "recorder@example.com",
            AccountUserId = "users/me-123",
            ClientId = "desktop-client-id",
            ClientSecret = "desktop-client-secret",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
            GrantedScopes = ["openid"]
        };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RefreshHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<CapturedRefreshRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRefreshRequest(request.Method, request.RequestUri, body));
            return responseFactory(request);
        }
    }

    private sealed record CapturedRefreshRequest(HttpMethod Method, Uri? Uri, string Body);
}
