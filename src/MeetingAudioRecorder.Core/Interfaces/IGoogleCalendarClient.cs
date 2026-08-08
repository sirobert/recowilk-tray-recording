using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IGoogleCalendarClient
{
    Task<IReadOnlyList<GoogleCalendarMeeting>> ListMeetingCandidatesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
