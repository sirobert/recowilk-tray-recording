using System.Net;
using System.Text;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Infrastructure.Recowilk;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingAudioRecorder.Audio.Tests;

public sealed class RecowilkUploadQueueTests
{
    [Fact]
    public async Task Connection_test_uses_api_key_header_and_requires_ping_success()
    {
        HttpRequestMessage? captured = null;
        var client = new HttpClient(new Handler(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK,
                """{"status":"ok","apiVersion":"v1","organizationId":"11111111-1111-1111-1111-111111111111","apiKeyId":"22222222-2222-2222-2222-222222222222","meetingOwnerId":"33333333-3333-3333-3333-333333333333"}""");
        }));
        await using var queue = new RecowilkUploadQueue(new Factory(client), new Settings(),
            new Credentials("rwk_live_123456789abc.secret"), NullLogger<RecowilkUploadQueue>.Instance);

        var result = await queue.TestConnectionAsync("https://minuteo.example", null);

        Assert.True(result.Success);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.OrganizationId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), result.ApiKeyId);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), result.MeetingOwnerId);
        Assert.Equal("ApiKey", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("rwk_live_123456789abc.secret", captured.Headers.Authorization.Parameter);
        Assert.Equal("/api/v1/ingest/ping", captured.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Connection_test_rejects_non_success_response()
    {
        var client = new HttpClient(new Handler(_ => new(HttpStatusCode.NotFound)));
        await using var queue = new RecowilkUploadQueue(new Factory(client), new Settings(),
            new Credentials("rwk_live_123456789abc.secret"), NullLogger<RecowilkUploadQueue>.Instance);

        Assert.False((await queue.TestConnectionAsync("https://minuteo.example", null)).Success);
    }

    [Fact]
    public async Task Plain_http_is_rejected_outside_loopback()
    {
        var calls = 0;
        var client = new HttpClient(new Handler(_ => { calls++; return new(HttpStatusCode.NotFound); }));
        await using var queue = new RecowilkUploadQueue(new Factory(client), new Settings(),
            new Credentials("key"), NullLogger<RecowilkUploadQueue>.Instance);
        Assert.False((await queue.TestConnectionAsync("http://example.com", null)).Success);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Connection_test_rejects_incomplete_success_payload()
    {
        var client = new HttpClient(new Handler(_ => Json(HttpStatusCode.OK, """{"status":"ok","apiVersion":"v1"}""")));
        await using var queue = new RecowilkUploadQueue(new Factory(client), new Settings(),
            new Credentials("key"), NullLogger<RecowilkUploadQueue>.Instance);

        var result = await queue.TestConnectionAsync("https://minuteo.example", null);

        Assert.False(result.Success);
        Assert.Equal(RecowilkConnectionFailure.InvalidResponse, result.Failure);
    }

    [Fact]
    public async Task Connection_test_accepts_only_200_ok()
    {
        var client = new HttpClient(new Handler(_ => Json(HttpStatusCode.Created,
            """{"status":"ok","apiVersion":"v1","organizationId":"11111111-1111-1111-1111-111111111111","apiKeyId":"22222222-2222-2222-2222-222222222222","meetingOwnerId":"33333333-3333-3333-3333-333333333333"}""")));
        await using var queue = new RecowilkUploadQueue(new Factory(client), new Settings(),
            new Credentials("key"), NullLogger<RecowilkUploadQueue>.Instance);

        var result = await queue.TestConnectionAsync("https://minuteo.example", null);

        Assert.False(result.Success);
        Assert.Equal(RecowilkConnectionFailure.InvalidResponse, result.Failure);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string value) => new(status)
    {
        Content = new StringContent(value, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => client; }
    private sealed class Credentials(string value) : IRecowilkCredentialStore
    { public bool HasKey => true; public string? Load() => value; public void Save(string value) { } public void Clear() { } }
    private sealed class Settings : ISettingsService
    {
        public AppSettings Current => AppSettings.CreateDefault();
        public event EventHandler? SettingsChanged { add { } remove { } }
        public AppSettings Load() => Current;
        public void Save(AppSettings settings) { }
        public ValidationResult Validate(AppSettings settings) => ValidationResult.Success();
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> result) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(result(request)); }
}
