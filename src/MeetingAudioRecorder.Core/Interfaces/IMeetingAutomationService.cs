using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IMeetingAutomationService : IAsyncDisposable
{
    event EventHandler<MeetingAutomationStatus>? StatusChanged;

    MeetingAutomationStatus Status { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task CheckNowAsync(CancellationToken cancellationToken = default);
}
