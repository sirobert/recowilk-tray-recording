using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IGoogleTokenStore
{
    Task<GoogleOAuthToken?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(GoogleOAuthToken token, CancellationToken cancellationToken = default);
    Task DeleteAsync(CancellationToken cancellationToken = default);
}
