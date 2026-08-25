using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IRecowilkCredentialStore
{
    bool HasKey { get; }
    string? Load();
    void Save(string value);
    void Clear();
}

public interface IRecowilkUploadQueue : IAsyncDisposable
{
    void Start();
    void Enqueue(RecordingCompletedEventArgs completed);
    Task<RecowilkConnectionResult> TestConnectionAsync(string baseUrl, string? candidateKey, CancellationToken cancellationToken = default);
}

public enum RecowilkConnectionFailure
{
    None,
    InvalidConfiguration,
    Unauthorized,
    Forbidden,
    RateLimited,
    InvalidResponse,
    ServerError,
    NetworkError
}

public sealed record RecowilkConnectionResult(
    bool Success,
    RecowilkConnectionFailure Failure = RecowilkConnectionFailure.None,
    Guid? OrganizationId = null,
    Guid? ApiKeyId = null,
    Guid? MeetingOwnerId = null,
    string? ApiVersion = null,
    string? TraceId = null,
    TimeSpan? RetryAfter = null)
{
    public static RecowilkConnectionResult Invalid(RecowilkConnectionFailure failure, string? traceId = null,
        TimeSpan? retryAfter = null) => new(false, failure, TraceId: traceId, RetryAfter: retryAfter);
}
