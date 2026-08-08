using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IGoogleMeetClient
{
    Task<GoogleMeetPresence> GetCurrentUserPresenceAsync(
        string meetingCode,
        string accountUserId,
        CancellationToken cancellationToken = default);
}
