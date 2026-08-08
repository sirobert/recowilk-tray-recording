namespace MeetingAudioRecorder.Core.Models;

public sealed record GoogleCalendarMeeting(
    string EventId,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string MeetingUri,
    string MeetingCode);

public sealed record GoogleMeetPresence(
    string MeetingCode,
    string? ConferenceRecordName,
    MeetingPresenceStatus Status);
