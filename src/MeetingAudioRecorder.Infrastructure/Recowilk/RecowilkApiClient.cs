using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MeetingAudioRecorder.Core.Interfaces;

namespace MeetingAudioRecorder.Infrastructure.Recowilk;

internal sealed class RecowilkApiClient(IHttpClientFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RecowilkConnectionResult> PingAsync(Uri baseUri, string key, CancellationToken cancellationToken)
    {
        try
        {
            var value = await SendAsync<PingResponse>(HttpMethod.Get, new(baseUri, "api/v1/ingest/ping"), key,
                null, null, cancellationToken, HttpStatusCode.OK).ConfigureAwait(false);
            return string.Equals(value.Status, "ok", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(value.ApiVersion, "v1", StringComparison.OrdinalIgnoreCase)
                   && value.OrganizationId != Guid.Empty && value.ApiKeyId != Guid.Empty && value.MeetingOwnerId != Guid.Empty
                ? new(true, OrganizationId: value.OrganizationId, ApiKeyId: value.ApiKeyId,
                    MeetingOwnerId: value.MeetingOwnerId, ApiVersion: value.ApiVersion)
                : RecowilkConnectionResult.Invalid(RecowilkConnectionFailure.InvalidResponse);
        }
        catch (RecowilkApiException ex)
        {
            var failure = ex.StatusCode switch
            {
                HttpStatusCode.Unauthorized => RecowilkConnectionFailure.Unauthorized,
                HttpStatusCode.Forbidden => RecowilkConnectionFailure.Forbidden,
                HttpStatusCode.TooManyRequests => RecowilkConnectionFailure.RateLimited,
                >= HttpStatusCode.InternalServerError => RecowilkConnectionFailure.ServerError,
                _ => RecowilkConnectionFailure.InvalidResponse
            };
            return RecowilkConnectionResult.Invalid(failure, ex.TraceId, ex.RetryAfter);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (JsonException) { return RecowilkConnectionResult.Invalid(RecowilkConnectionFailure.InvalidResponse); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { return RecowilkConnectionResult.Invalid(RecowilkConnectionFailure.NetworkError); }
    }

    public Task<CreateMeetingResponse> CreateMeetingAsync(Uri baseUri, string key, object payload,
        string idempotencyKey, CancellationToken ct) => SendAsync<CreateMeetingResponse>(HttpMethod.Post,
        new(baseUri, "api/v1/ingest/meetings"), key, JsonContent.Create(payload, options: JsonOptions), idempotencyKey, ct);

    public Task<InitUploadResponse> InitUploadAsync(Uri baseUri, string key, Guid meetingId, object payload,
        string idempotencyKey, CancellationToken ct) => SendAsync<InitUploadResponse>(HttpMethod.Post,
        new(baseUri, $"api/v1/ingest/meetings/{meetingId:D}/uploads"), key,
        JsonContent.Create(payload, options: JsonOptions), idempotencyKey, ct);

    public Task<UploadStatusResponse> GetStatusAsync(Uri baseUri, string key, Guid uploadId, CancellationToken ct) =>
        SendAsync<UploadStatusResponse>(HttpMethod.Get, new(baseUri, $"api/v1/ingest/uploads/{uploadId:D}"),
            key, null, null, ct);

    public async Task PutChunkAsync(Uri baseUri, string key, Guid uploadId, int index, byte[] buffer,
        string hash, CancellationToken ct)
    {
        using var content = new ByteArrayContent(buffer);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var request = CreateRequest(HttpMethod.Put,
            new(baseUri, $"api/v1/ingest/uploads/{uploadId:D}/chunks/{index}"), key, content, null);
        request.Headers.TryAddWithoutValidation("Content-SHA256", hash);
        using var response = await Client().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task CompleteAsync(Uri baseUri, string key, Guid uploadId, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Post,
            new(baseUri, $"api/v1/ingest/uploads/{uploadId:D}/complete?startProcessing=true"), key, null, null);
        using var response = await Client().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, Uri uri, string key, HttpContent? content,
        string? idempotencyKey, CancellationToken ct, HttpStatusCode? requiredStatus = null)
    {
        using var request = CreateRequest(method, uri, key, content, idempotencyKey);
        using var response = await Client().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        if (requiredStatus is not null && response.StatusCode != requiredStatus)
            throw new RecowilkApiException(response.StatusCode, "unexpected_status", null, null);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false)
               ?? throw new JsonException("RecoWilk zwrócił pustą odpowiedź.");
    }

    private HttpClient Client() => factory.CreateClient("recowilk");

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string key, HttpContent? content,
        string? idempotencyKey)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", key);
        if (idempotencyKey is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string? code = null;
        string? traceId = null;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = document.RootElement;
            code = GetString(root, "code") ?? GetString(root, "errorCode") ?? GetString(root, "title");
            traceId = GetString(root, "traceId");
        }
        catch (JsonException) { }
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } date) retryAfter = date - DateTimeOffset.UtcNow;
        throw new RecowilkApiException(response.StatusCode, code, traceId, retryAfter);
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

internal sealed class RecowilkApiException(HttpStatusCode statusCode, string? code, string? traceId, TimeSpan? retryAfter)
    : Exception($"RecoWilk HTTP {(int)statusCode}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? Code { get; } = code;
    public string? TraceId { get; } = traceId;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

internal sealed record PingResponse(string Status, string ApiVersion, Guid OrganizationId, Guid ApiKeyId, Guid MeetingOwnerId);
internal sealed record CreateMeetingResponse(Guid MeetingId, bool Created);
internal sealed record InitUploadResponse(Guid UploadId, int ChunkSize, int TotalChunks, DateTimeOffset? ExpiresAt);
internal sealed record UploadStatusResponse(Guid UploadId, string Status, int[] ReceivedChunks, int[] MissingChunks);
