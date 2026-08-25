namespace MeetingAudioRecorder.Core.Models;

/// <summary>
/// Metadane sesji nagrywania.
/// </summary>
public sealed class RecordingSessionInfo
{
    public Guid RecordingId { get; init; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public string MicrophoneDeviceId { get; init; } = string.Empty;
    public string OutputDeviceId { get; init; } = string.Empty;
    public string MicrophoneTempPath { get; init; } = string.Empty;
    public string LoopbackTempPath { get; init; } = string.Empty;
    public required RecordingSettingsSnapshot SettingsSnapshot { get; init; }
    public string? OutputMp3Path { get; set; }
    public TimeSpan? Duration { get; set; }
    public long MicrophoneStartOffsetTicks { get; set; }
    public long LoopbackStartOffsetTicks { get; set; }
    public string? ErrorMessage { get; set; }
    public RecordingSourceContext? SourceContext { get; set; }
}

public sealed record RecordingSourceContext(
    string Provider,
    string Client,
    string? ExternalEventId,
    string? MeetingUrl,
    string Title,
    string? Description,
    DateTimeOffset? ScheduledAt,
    IReadOnlyList<GoogleMeetingAttendee> Participants);

public sealed class RecordingCompletedEventArgs(RecordingResult result, RecordingSessionInfo session) : EventArgs
{
    public RecordingResult Result { get; } = result;
    public RecordingSessionInfo Session { get; } = session;
}

/// <summary>
/// Wynik operacji nagrywania/przetwarzania.
/// </summary>
public sealed class RecordingResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public Guid RecordingId { get; init; }
    public IReadOnlyList<string> AdditionalFiles { get; init; } = Array.Empty<string>();

    public static RecordingResult Ok(Guid id, string path, TimeSpan duration, IReadOnlyList<string>? additional = null)
        => new()
        {
            Success = true,
            RecordingId = id,
            OutputPath = path,
            Duration = duration,
            AdditionalFiles = additional ?? Array.Empty<string>()
        };

    public static RecordingResult Fail(Guid id, string message)
        => new()
        {
            Success = false,
            RecordingId = id,
            ErrorMessage = message
        };
}
