namespace MeetingAudioRecorder.Core.Models;

public sealed record MeetingAutomationStatus(
    MeetingAutomationState State,
    string Message,
    string? MeetingTitle = null);
