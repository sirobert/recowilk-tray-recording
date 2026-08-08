using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IGoogleAuthorizationService
{
    Task<GoogleConnectionInfo> GetConnectionInfoAsync(CancellationToken cancellationToken = default);
    Task<GoogleConnectionInfo> ConnectAsync(
        string clientConfigurationPath,
        CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
