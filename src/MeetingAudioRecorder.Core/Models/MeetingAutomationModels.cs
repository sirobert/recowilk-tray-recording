namespace MeetingAudioRecorder.Core.Models;

public enum MeetingPresenceStatus
{
    Unknown = 0,
    Absent = 1,
    Present = 2,
    ConferenceEnded = 3
}

public enum MeetingAutomationAction
{
    None = 0,
    StartRecording = 1,
    StopRecording = 2
}

public enum MeetingAutomationState
{
    Disabled = 0,
    WaitingForMeeting = 1,
    WaitingForJoin = 2,
    StartRequested = 3,
    RecordingAutomatically = 4,
    ConfirmingDisconnect = 5,
    ManualRecordingActive = 6,
    SuppressedUntilLeave = 7,
    AuthenticationRequired = 8,
    ApiUnavailable = 9
}

public sealed class MeetingAutomationObservation
{
    public required TimeSpan MonotonicNow { get; init; }
    public required bool Enabled { get; init; }
    public required bool IsAuthenticated { get; init; }
    public required bool IsApiAvailable { get; init; }
    public string? MeetingId { get; init; }
    public required MeetingPresenceStatus Presence { get; init; }
    public Guid? CurrentRecordingId { get; init; }
    public required bool CanStartRecording { get; init; }
}

public sealed record MeetingAutomationDecision(
    MeetingAutomationAction Action,
    MeetingAutomationState State,
    string? MeetingId,
    Guid? OwnedRecordingId);
