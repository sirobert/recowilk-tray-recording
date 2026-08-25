using System.Net;
using System.Net.Http.Json;
using System.Text;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Infrastructure.Recowilk;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingAudioRecorder.Audio.Tests;

public sealed class RecowilkUploadFlowTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mar-recowilk-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Full_upload_is_tenant_bound_encrypted_and_keeps_local_mp3()
    {
        Directory.CreateDirectory(_directory);
        var audio = Path.Combine(_directory, "meeting.mp3");
        await File.WriteAllBytesAsync(audio, Enumerable.Range(0, 700_000).Select(i => (byte)(i % 251)).ToArray());
        var requests = new List<HttpRequestMessage>();
        var handler = new ScriptedHandler(async request =>
        {
            requests.Add(await CloneAsync(request));
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/ingest/ping" => Json(HttpStatusCode.OK, Ping()),
                "/api/v1/ingest/meetings" => Json(HttpStatusCode.Created,
                    """{"meetingId":"44444444-4444-4444-4444-444444444444","created":true}"""),
                "/api/v1/ingest/meetings/44444444-4444-4444-4444-444444444444/uploads" => Json(HttpStatusCode.Created,
                    """{"uploadId":"55555555-5555-5555-5555-555555555555","chunkSize":262144,"totalChunks":3,"expiresAt":"2026-08-27T12:00:00Z"}"""),
                "/api/v1/ingest/uploads/55555555-5555-5555-5555-555555555555" => Json(HttpStatusCode.OK,
                    """{"uploadId":"55555555-5555-5555-5555-555555555555","status":"Pending","receivedChunks":[],"missingChunks":[0,1,2]}"""),
                "/api/v1/ingest/uploads/55555555-5555-5555-5555-555555555555/complete" => Json(HttpStatusCode.Accepted,
                    """{"audioAssetId":"66666666-6666-6666-6666-666666666666","processingJobId":"77777777-7777-7777-7777-777777777777"}"""),
                _ when request.Method == HttpMethod.Put => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        var settings = new MutableSettings("https://minuteo.example");
        await using var queue = new RecowilkUploadQueue(new Factory(new HttpClient(handler)), settings,
            new Credentials("secret-key"), NullLogger<RecowilkUploadQueue>.Instance,
            _directory, new PassthroughProtector());

        queue.Enqueue(Completed(audio));
        var queueFile = Assert.Single(Directory.EnumerateFiles(_directory, "*.upload"));
        var persisted = await File.ReadAllTextAsync(queueFile);
        Assert.DoesNotContain("alice@example.com", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-key", persisted, StringComparison.Ordinal);

        await queue.ProcessPendingOnceAsync();

        Assert.True(File.Exists(audio));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.upload"));
        var init = Assert.Single(requests, r => r.RequestUri!.AbsolutePath.EndsWith("/uploads", StringComparison.Ordinal));
        Assert.Equal("recorder:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa:audio:1",
            Assert.Single(init.Headers.GetValues("Idempotency-Key")));
        Assert.Equal(3, requests.Count(r => r.Method == HttpMethod.Put));
        Assert.All(requests.Where(r => r.Method == HttpMethod.Put), request =>
            Assert.Matches("^[0-9a-f]{64}$", Assert.Single(request.Headers.GetValues("Content-SHA256"))));
    }

    [Fact]
    public async Task Expired_upload_starts_next_numbered_session()
    {
        var audio = await WriteAudioAsync(1000);
        var initCount = 0;
        var sessionKeys = new List<string>();
        var handler = new ScriptedHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/uploads", StringComparison.Ordinal))
            {
                initCount++;
                sessionKeys.Add(Assert.Single(request.Headers.GetValues("Idempotency-Key")));
                var id = initCount == 1 ? "55555555-5555-5555-5555-555555555555" : "88888888-8888-8888-8888-888888888888";
                return Task.FromResult(Json(HttpStatusCode.Created,
                    $$"""{"uploadId":"{{id}}","chunkSize":262144,"totalChunks":1,"expiresAt":"2026-08-27T12:00:00Z"}"""));
            }
            if (path.EndsWith("55555555-5555-5555-5555-555555555555", StringComparison.Ordinal))
                return Task.FromResult(Problem(HttpStatusCode.Gone, "upload_expired"));
            return Task.FromResult(StandardResponse(request, "88888888-8888-8888-8888-888888888888"));
        });
        await using var queue = CreateQueue(handler, new MutableSettings("https://minuteo.example"));
        queue.Enqueue(Completed(audio));

        await queue.ProcessPendingOnceAsync();

        Assert.Equal(2, initCount);
        Assert.Equal(["recorder:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa:audio:1",
            "recorder:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa:audio:2"], sessionKeys);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.upload"));
    }

    [Fact]
    public async Task Incomplete_finalization_returns_to_status_and_uploads_missing_chunk()
    {
        var audio = await WriteAudioAsync(1000);
        var statusCalls = 0;
        var completeCalls = 0;
        var putCalls = 0;
        var handler = new ScriptedHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/complete", StringComparison.Ordinal))
            {
                completeCalls++;
                return Task.FromResult(completeCalls == 1
                    ? Problem(HttpStatusCode.UnprocessableEntity, "upload_incomplete")
                    : Json(HttpStatusCode.Accepted, "{}"));
            }
            if (request.Method == HttpMethod.Put)
            {
                putCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if (path == "/api/v1/ingest/uploads/55555555-5555-5555-5555-555555555555")
            {
                statusCalls++;
                var missing = statusCalls == 1 ? "[]" : "[0]";
                return Task.FromResult(Json(HttpStatusCode.OK,
                    $$"""{"uploadId":"55555555-5555-5555-5555-555555555555","status":"Pending","receivedChunks":[],"missingChunks":{{missing}}}"""));
            }
            return Task.FromResult(StandardResponse(request));
        });
        await using var queue = CreateQueue(handler, new MutableSettings("https://minuteo.example"));
        queue.Enqueue(Completed(audio));

        await queue.ProcessPendingOnceAsync();

        Assert.Equal(2, statusCalls);
        Assert.Equal(2, completeCalls);
        Assert.Equal(1, putCalls);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.upload"));
    }

    [Fact]
    public async Task Changing_tenant_discards_remote_ids_but_keeps_external_id()
    {
        var audio = await WriteAudioAsync(1000);
        var settings = new MutableSettings("https://first.example");
        var first = true;
        var createdHosts = new List<string>();
        var handler = new ScriptedHandler(request =>
        {
            var host = request.RequestUri!.Host;
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v1/ingest/ping")
                return Task.FromResult(Json(HttpStatusCode.OK, Ping(host == "first.example"
                    ? "11111111-1111-1111-1111-111111111111"
                    : "99999999-9999-9999-9999-999999999999")));
            if (path == "/api/v1/ingest/meetings") createdHosts.Add(host);
            if (host == "first.example" && path.StartsWith("/api/v1/ingest/uploads/", StringComparison.Ordinal))
            {
                first = false;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }
            var uploadId = host == "first.example"
                ? "55555555-5555-5555-5555-555555555555"
                : "88888888-8888-8888-8888-888888888888";
            return Task.FromResult(StandardResponse(request, uploadId,
                host == "first.example" ? "44444444-4444-4444-4444-444444444444" : "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        });
        await using var queue = CreateQueue(handler, settings);
        queue.Enqueue(Completed(audio));
        await queue.ProcessPendingOnceAsync();
        Assert.False(first);

        settings.ChangeUrl("https://second.example");
        await queue.ProcessPendingOnceAsync();

        Assert.Equal(["first.example", "second.example"], createdHosts);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.upload"));
    }

    [Fact]
    public async Task Invalid_missing_index_is_permanent_and_does_not_send_chunk()
    {
        var audio = await WriteAudioAsync(1000);
        var puts = 0;
        var handler = new ScriptedHandler(request =>
        {
            if (request.Method == HttpMethod.Put) puts++;
            if (request.RequestUri!.AbsolutePath == "/api/v1/ingest/uploads/55555555-5555-5555-5555-555555555555")
                return Task.FromResult(Json(HttpStatusCode.OK,
                    """{"uploadId":"55555555-5555-5555-5555-555555555555","status":"Pending","receivedChunks":[],"missingChunks":[1]}"""));
            return Task.FromResult(StandardResponse(request));
        });
        await using var queue = CreateQueue(handler, new MutableSettings("https://minuteo.example"));
        queue.Enqueue(Completed(audio));

        await queue.ProcessPendingOnceAsync();

        Assert.Equal(0, puts);
        Assert.Single(Directory.EnumerateFiles(_directory, "*.upload"));
    }

    [Fact]
    public async Task Corrupt_entry_does_not_block_valid_upload_and_dispose_is_idempotent()
    {
        var audio = await WriteAudioAsync(1000);
        await File.WriteAllTextAsync(Path.Combine(_directory, "broken.upload"), "not-protected-data");
        var queue = CreateQueue(new ScriptedHandler(request => Task.FromResult(StandardResponse(request))),
            new MutableSettings("https://minuteo.example"));
        queue.Enqueue(Completed(audio));

        await queue.ProcessPendingOnceAsync();
        await queue.DisposeAsync();
        await queue.DisposeAsync();

        Assert.Empty(Directory.EnumerateFiles(_directory, "*.upload"));
        Assert.Single(Directory.EnumerateFiles(_directory, "*.corrupt.*"));
        Assert.True(File.Exists(audio));
    }

    [Fact]
    public async Task Legacy_plaintext_entry_is_migrated_and_unauthorized_state_does_not_poll_again()
    {
        var audio = await WriteAudioAsync(1000);
        var legacyPath = Path.Combine(_directory, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json");
        await File.WriteAllTextAsync(legacyPath, $$"""
        {
          "recordingId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "audioPath":"{{audio.Replace("\\", "\\\\")}}",
          "startedAt":"2026-08-25T10:01:00Z",
          "durationMs":1000,
          "source":{"provider":"GoogleMeet","client":"MeetingAudioRecorder","title":"Planowanie","description":"opis","participants":[{"displayName":"Alice","email":"alice@example.com","isOrganizer":true}]}
        }
        """);
        var calls = 0;
        var handler = new ScriptedHandler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        await using var queue = CreateQueue(handler, new MutableSettings("https://minuteo.example"));

        await queue.ProcessPendingOnceAsync();
        await queue.ProcessPendingOnceAsync();

        Assert.Equal(1, calls);
        Assert.False(File.Exists(legacyPath));
        var migrated = Assert.Single(Directory.EnumerateFiles(_directory, "*.upload"));
        Assert.DoesNotContain("alice@example.com", await File.ReadAllTextAsync(migrated), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(audio));
    }

    [Theory]
    [InlineData(262143, 1)]
    [InlineData(16777217, 1)]
    [InlineData(262144, 2)]
    public void Invalid_upload_geometry_is_rejected(int chunkSize, int totalChunks)
    {
        Assert.Throws<InvalidDataException>(() => RecowilkUploadQueue.ValidateUploadGeometry(
            1000, Guid.Parse("55555555-5555-5555-5555-555555555555"), chunkSize, totalChunks));
    }

    [Fact]
    public async Task Existing_pending_upload_is_migrated_to_catalog_while_export_is_disabled()
    {
        var audio = await WriteAudioAsync(1000);
        var legacyPath = Path.Combine(_directory, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json");
        await File.WriteAllTextAsync(legacyPath, System.Text.Json.JsonSerializer.Serialize(new
        {
            recordingId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            audioPath = audio,
            startedAt = DateTimeOffset.Parse("2026-08-25T10:01:00Z"),
            durationMs = 1000,
            source = new { provider = "GoogleMeet", client = "MeetingAudioRecorder", title = "Planowanie", participants = Array.Empty<object>() }
        }));
        var calls = 0;
        var catalog = new ProtectedFileRecordingCatalog(Path.Combine(_directory, "catalog"), new CatalogProtector());
        await using var queue = new RecowilkUploadQueue(new Factory(new HttpClient(new ScriptedHandler(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }))), new MutableSettings("https://minuteo.example", enabled: false), new Credentials("secret-key"),
            NullLogger<RecowilkUploadQueue>.Instance, _directory, new PassthroughProtector(), catalog);

        await queue.ProcessPendingOnceAsync();

        var imported = Assert.Single(catalog.List());
        Assert.Equal("Planowanie", imported.Title);
        Assert.Equal(RecordingExportStatus.Queued, imported.ExportStatus);
        Assert.Equal(0, calls);
        Assert.False(File.Exists(legacyPath));
        Assert.Single(Directory.EnumerateFiles(_directory, "*.upload"));
    }

    [Fact]
    public async Task Server_failure_is_visible_and_manual_retry_reuses_recording_identity()
    {
        var audio = await WriteAudioAsync(1000);
        var catalogDirectory = Path.Combine(_directory, "catalog");
        var catalog = new ProtectedFileRecordingCatalog(catalogDirectory, new CatalogProtector());
        var failMeeting = true;
        var externalIds = new List<string>();
        var handler = new ScriptedHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/ingest/meetings")
            {
                externalIds.Add((await request.Content!.ReadFromJsonAsync<Dictionary<string, object>>())!["externalId"].ToString()!);
                if (failMeeting) return Problem(HttpStatusCode.InternalServerError, "server_error");
            }
            return StandardResponse(request);
        });
        await using var queue = new RecowilkUploadQueue(new Factory(new HttpClient(handler)),
            new MutableSettings("https://minuteo.example"), new Credentials("secret-key"),
            NullLogger<RecowilkUploadQueue>.Instance, _directory, new PassthroughProtector(), catalog);
        queue.Enqueue(Completed(audio));

        await queue.ProcessPendingOnceAsync();

        var failed = Assert.Single(catalog.List());
        Assert.Equal(RecordingExportStatus.RetryScheduled, failed.ExportStatus);
        Assert.Equal(500, failed.HttpStatusCode);
        Assert.Equal("trace-test", failed.TraceId);
        failMeeting = false;

        var retry = queue.RetryExport(failed.RecordingId);
        await queue.ProcessPendingOnceAsync();

        Assert.True(retry.Success);
        var exported = Assert.Single(catalog.List());
        Assert.Equal(RecordingExportStatus.Exported, exported.ExportStatus);
        Assert.Equal(Guid.Parse("44444444-4444-4444-4444-444444444444"), exported.MeetingId);
        Assert.All(externalIds, id => Assert.Equal("recorder:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", id));
        Assert.True(File.Exists(audio));
    }

    private async Task<string> WriteAudioAsync(int length)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "meeting.mp3");
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray());
        return path;
    }

    private RecowilkUploadQueue CreateQueue(HttpMessageHandler handler, MutableSettings settings) =>
        new(new Factory(new HttpClient(handler)), settings, new Credentials("secret-key"),
            NullLogger<RecowilkUploadQueue>.Instance, _directory, new PassthroughProtector());

    private static HttpResponseMessage StandardResponse(HttpRequestMessage request,
        string uploadId = "55555555-5555-5555-5555-555555555555",
        string meetingId = "44444444-4444-4444-4444-444444444444")
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/api/v1/ingest/ping") return Json(HttpStatusCode.OK, Ping());
        if (path == "/api/v1/ingest/meetings")
            return Json(HttpStatusCode.Created, $$"""{"meetingId":"{{meetingId}}","created":true}""");
        if (path.EndsWith("/uploads", StringComparison.Ordinal))
            return Json(HttpStatusCode.Created,
                $$"""{"uploadId":"{{uploadId}}","chunkSize":262144,"totalChunks":1,"expiresAt":"2026-08-27T12:00:00Z"}""");
        if (path == $"/api/v1/ingest/uploads/{uploadId}")
            return Json(HttpStatusCode.OK,
                $$"""{"uploadId":"{{uploadId}}","status":"Pending","receivedChunks":[],"missingChunks":[]}""");
        if (path.EndsWith("/complete", StringComparison.Ordinal)) return Json(HttpStatusCode.Accepted, "{}");
        if (request.Method == HttpMethod.Put) return new HttpResponseMessage(HttpStatusCode.NoContent);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string code) => new(status)
    {
        Content = new StringContent($$"""{"code":"{{code}}","traceId":"trace-test"}""",
            System.Text.Encoding.UTF8, "application/problem+json")
    };

    private static RecordingCompletedEventArgs Completed(string audio)
    {
        var source = new RecordingSourceContext("GoogleMeet", "MeetingAudioRecorder", "event-1",
            "https://meet.google.com/abc-defg-hij", "Planowanie", "Prywatny opis",
            DateTimeOffset.Parse("2026-08-25T10:00:00Z"),
            [new GoogleMeetingAttendee("Alice", "alice@example.com", true)]);
        var session = new RecordingSessionInfo
        {
            RecordingId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            StartedAt = DateTimeOffset.Parse("2026-08-25T10:01:00Z"),
            StoppedAt = DateTimeOffset.Parse("2026-08-25T10:31:00Z"),
            SettingsSnapshot = new RecordingSettingsSnapshot
            {
                MicrophoneDeviceId = "mic",
                OutputDeviceId = "out",
                RecordingsDirectory = Path.GetDirectoryName(audio)!,
                FileNameFormat = "meeting.mp3"
            },
            SourceContext = source
        };
        return new RecordingCompletedEventArgs(
            RecordingResult.Ok(session.RecordingId, audio, TimeSpan.FromMinutes(30)), session);
    }

    private static string Ping(string organizationId = "11111111-1111-1111-1111-111111111111") =>
        $$"""{"status":"ok","apiVersion":"v1","organizationId":"{{organizationId}}","apiKeyId":"22222222-2222-2222-2222-222222222222","meetingOwnerId":"33333333-3333-3333-3333-333333333333"}""";

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
    };

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var header in source.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (source.Content is not null)
            clone.Content = new ByteArrayContent(await source.Content.ReadAsByteArrayAsync());
        return clone;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Credentials(string value) : IRecowilkCredentialStore
    {
        public bool HasKey => true;
        public string? Load() => value;
        public void Save(string newValue) { }
        public void Clear() { }
    }

    private sealed class MutableSettings(string url, bool enabled = true) : ISettingsService
    {
        private readonly AppSettings _settings = new()
        {
            RecowilkUploadEnabled = enabled,
            RecowilkBaseUrl = url
        };
        public AppSettings Current => _settings;
        public event EventHandler? SettingsChanged;
        public AppSettings Load() => _settings;
        public void Save(AppSettings settings) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        public ValidationResult Validate(AppSettings settings) => ValidationResult.Success();
        public void ChangeUrl(string value)
        {
            _settings.RecowilkBaseUrl = value;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class PassthroughProtector : IRecowilkQueueProtector
    {
        public byte[] Protect(byte[] value) => System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(value));
        public byte[] Unprotect(byte[] value) => Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(value));
    }

    private sealed class CatalogProtector : IRecordingCatalogProtector
    {
        public byte[] Protect(byte[] value) => System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(value));
        public byte[] Unprotect(byte[] value) => Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(value));
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }
}
