using System.Net;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Infrastructure.Google;
using Moq;

namespace MeetingAudioRecorder.Audio.Tests;

public sealed class GoogleOAuthAuthorizationServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "mar-google-oauth-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Connect_UsesPkceAndStoresAuthenticatedIdentity()
    {
        var configurationPath = await WriteConfigurationAsync();
        var tokenStore = new Mock<IGoogleTokenStore>();
        GoogleOAuthToken? saved = null;
        tokenStore.Setup(value => value.SaveAsync(It.IsAny<GoogleOAuthToken>(), It.IsAny<CancellationToken>()))
            .Callback<GoogleOAuthToken, CancellationToken>((token, _) => saved = token)
            .Returns(Task.CompletedTask);
        var consent = new FakeUserConsent();
        var handler = new OAuthHandler(request =>
        {
            if (request.RequestUri!.AbsoluteUri == "https://oauth2.googleapis.com/token")
            {
                return Json("""
                    {
                      "access_token": "access-from-code",
                      "refresh_token": "refresh-from-code",
                      "expires_in": 3600,
                      "scope": "openid email profile https://www.googleapis.com/auth/calendar.events.readonly https://www.googleapis.com/auth/meetings.space.readonly",
                      "token_type": "Bearer"
                    }
                    """);
            }

            if (request.RequestUri.AbsoluteUri == "https://openidconnect.googleapis.com/v1/userinfo")
                return Json("{ \"sub\": \"123456789\", \"email\": \"recorder@example.com\" }");

            throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
        });
        var service = new GoogleOAuthAuthorizationService(
            tokenStore.Object,
            new HttpClient(handler),
            consent,
            new FixedTimeProvider(Now));

        var connection = await service.ConnectAsync(configurationPath);

        Assert.True(connection.IsConnected);
        Assert.Equal("recorder@example.com", connection.AccountEmail);
        Assert.Equal("users/123456789", connection.AccountUserId);
        Assert.NotNull(consent.AuthorizationUri);
        var authorizationQuery = Uri.UnescapeDataString(consent.AuthorizationUri.Query);
        Assert.Contains("code_challenge_method=S256", authorizationQuery, StringComparison.Ordinal);
        Assert.Contains("access_type=offline", authorizationQuery, StringComparison.Ordinal);
        Assert.Contains("prompt=consent", authorizationQuery, StringComparison.Ordinal);
        Assert.Contains("calendar.events.readonly", authorizationQuery, StringComparison.Ordinal);
        Assert.Contains("meetings.space.readonly", authorizationQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("client-secret", authorizationQuery, StringComparison.Ordinal);

        var tokenRequest = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, tokenRequest.Method);
        Assert.Contains("code_verifier=", tokenRequest.Body, StringComparison.Ordinal);
        Assert.Contains("code=authorization-code", tokenRequest.Body, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A54321%2Foauth2%2Fcallback%2F", tokenRequest.Body, StringComparison.Ordinal);
        Assert.NotNull(saved);
        Assert.Equal("access-from-code", saved.AccessToken);
        Assert.Equal("refresh-from-code", saved.RefreshToken);
        Assert.Equal("users/123456789", saved.AccountUserId);
        Assert.Equal("desktop-client-id", saved.ClientId);
        Assert.Equal("client-secret", saved.ClientSecret);
        Assert.Equal(Now.AddHours(1), saved.ExpiresAtUtc);
    }

    [Fact]
    public async Task Connect_InvalidTokenEndpointIsRejectedBeforeConsent()
    {
        var path = await WriteConfigurationAsync("https://evil.example/token");
        var consent = new FakeUserConsent();
        var service = new GoogleOAuthAuthorizationService(
            Mock.Of<IGoogleTokenStore>(),
            new HttpClient(new OAuthHandler(_ => throw new InvalidOperationException())),
            consent,
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ConnectAsync(path));

        Assert.Null(consent.AuthorizationUri);
    }

    [Fact]
    public async Task Disconnect_DeletesStoredToken()
    {
        var store = new Mock<IGoogleTokenStore>();
        store.Setup(value => value.DeleteAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var service = new GoogleOAuthAuthorizationService(
            store.Object,
            new HttpClient(new OAuthHandler(_ => throw new InvalidOperationException())),
            new FakeUserConsent(),
            new FixedTimeProvider(Now));

        await service.DisconnectAsync();

        store.Verify(value => value.DeleteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private async Task<string> WriteConfigurationAsync(
        string tokenUri = "https://oauth2.googleapis.com/token")
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "client_secret.json");
        var json = $$"""
            {
              "installed": {
                "client_id": "desktop-client-id",
                "project_id": "meeting-audio-recorder",
                "auth_uri": "https://accounts.google.com/o/oauth2/auth",
                "token_uri": "{{tokenUri}}",
                "client_secret": "client-secret",
                "redirect_uris": ["http://localhost"]
              }
            }
            """;
        await File.WriteAllTextAsync(path, json);
        return path;
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class FakeUserConsent : IGoogleOAuthUserConsent
    {
        public Uri? AuthorizationUri { get; private set; }

        public Task<GoogleOAuthAuthorizationCode> RequestCodeAsync(
            Func<Uri, Uri> authorizationUriFactory,
            string expectedState,
            CancellationToken cancellationToken)
        {
            var redirectUri = new Uri("http://127.0.0.1:54321/oauth2/callback/");
            AuthorizationUri = authorizationUriFactory(redirectUri);
            return Task.FromResult(new GoogleOAuthAuthorizationCode("authorization-code", redirectUri));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class OAuthHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri, body));
            return responseFactory(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string Body);
}
