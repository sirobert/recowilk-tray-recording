namespace MeetingAudioRecorder.Core.Models;

public sealed record BrowserMeetLink(
    string MeetingCode,
    string? Browser,
    DateTimeOffset ObservedAtUtc);
