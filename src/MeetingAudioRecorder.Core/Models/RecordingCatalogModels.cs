namespace MeetingAudioRecorder.Core.Models;

public enum RecordingExportStatus
{
    LocalOnly,
    Queued,
    Connecting,
    CreatingMeeting,
    InitializingUpload,
    Uploading,
    Completing,
    RetryScheduled,
    WaitingForCredentials,
    Exported,
    PermanentFailure,
    MissingFile
}

public sealed record RecordingCatalogParticipant(string DisplayName, string? Email, string Role);

public sealed class RecordingCatalogEntry
{
    public int SchemaVersion { get; set; } = 1;
    public Guid RecordingId { get; set; }
    public required string AudioPath { get; set; }
    public long AudioSizeBytes { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public long DurationMs { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public string Provider { get; set; } = "ManualRecorder";
    public string Client { get; set; } = "MeetingAudioRecorder";
    public string? ExternalEventId { get; set; }
    public string? MeetingUrl { get; set; }
    public IReadOnlyList<RecordingCatalogParticipant> Participants { get; set; } = [];
    public RecordingExportStatus ExportStatus { get; set; }
    public Guid? MeetingId { get; set; }
    public Guid? UploadId { get; set; }
    public Guid? AudioAssetId { get; set; }
    public Guid? ProcessingJobId { get; set; }
    public int UploadedChunks { get; set; }
    public int TotalChunks { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? ErrorCategory { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? TraceId { get; set; }
    public DateTimeOffset? ExportedAt { get; set; }

    public static RecordingCatalogEntry FromCompleted(RecordingCompletedEventArgs completed, RecordingExportStatus status)
    {
        var source = completed.Session.SourceContext;
        var path = completed.Result.OutputPath!;
        return new RecordingCatalogEntry
        {
            RecordingId = completed.Result.RecordingId,
            AudioPath = path,
            AudioSizeBytes = File.Exists(path) ? new FileInfo(path).Length : 0,
            StartedAt = completed.Session.StartedAt,
            StoppedAt = completed.Session.StoppedAt,
            DurationMs = (long)completed.Result.Duration.TotalMilliseconds,
            Title = string.IsNullOrWhiteSpace(source?.Title) ? Path.GetFileNameWithoutExtension(path) : source.Title,
            Description = source?.Description,
            ScheduledAt = source?.ScheduledAt,
            Provider = source?.Provider ?? "ManualRecorder",
            Client = source?.Client ?? "MeetingAudioRecorder",
            ExternalEventId = source?.ExternalEventId,
            MeetingUrl = source?.MeetingUrl,
            Participants = source?.Participants.Select(p => new RecordingCatalogParticipant(
                p.DisplayName, p.Email, p.IsOrganizer ? "Organizer" : "Attendee")).ToArray() ?? [],
            ExportStatus = status
        };
    }
}

public sealed class RecordingCatalogChangedEventArgs(Guid recordingId) : EventArgs
{
    public Guid RecordingId { get; } = recordingId;
}

public sealed record RecordingRetryResult(bool Success, string Message)
{
    public static RecordingRetryResult Ok(string message = "Eksport dodano do kolejki.") => new(true, message);
    public static RecordingRetryResult Failed(string message) => new(false, message);
}
