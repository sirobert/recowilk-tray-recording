using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IRecordingCoordinator : IAsyncDisposable
{
    event EventHandler<RecordingStateChangedEventArgs>? StateChanged;
    event EventHandler<TimeSpan>? DurationUpdated;
    event EventHandler<AudioLevelEventArgs>? MicrophoneLevelChanged;
    event EventHandler<AudioLevelEventArgs>? LoopbackLevelChanged;

    AppRecordingState State { get; }
    TimeSpan CurrentDuration { get; }
    RecordingSessionInfo? CurrentSession { get; }
    bool CanStart { get; }
    bool CanStop { get; }

    Task StartRecordingAsync(CancellationToken cancellationToken = default);
    Task StartRecordingWithDevicesAsync(
        RecordingDeviceSelection deviceSelection,
        CancellationToken cancellationToken = default);
    Task<RecordingResult> StopRecordingAsync(CancellationToken cancellationToken = default);
    Task<RecordingResult> RecoverRecordingAsync(RecoverableRecording recoverable, CancellationToken cancellationToken = default);
    Task ToggleRecordingAsync(CancellationToken cancellationToken = default);
}
