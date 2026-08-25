namespace MeetingAudioRecorder.Core.Models;

public sealed record GoogleCalendarMeeting(
    string EventId,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string MeetingUri,
    string MeetingCode)
{
    public string? Description { get; init; }
    public IReadOnlyList<GoogleMeetingAttendee> Attendees { get; init; } = [];
}

public sealed record GoogleMeetingAttendee(string DisplayName, string? Email, bool IsOrganizer);

public sealed record GoogleMeetPresence(
    string MeetingCode,
    string? ConferenceRecordName,
    MeetingPresenceStatus Status);
