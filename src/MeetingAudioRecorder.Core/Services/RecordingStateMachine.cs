using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Services;

/// <summary>
/// Maszyna stanów nagrywania z walidacją dozwolonych przejść.
/// </summary>
public sealed class RecordingStateMachine
{
    private readonly object _lock = new();
    private AppRecordingState _state = AppRecordingState.Idle;

    public AppRecordingState State
    {
        get { lock (_lock) return _state; }
    }

    public bool CanStart
    {
        get
        {
            lock (_lock)
                return _state is AppRecordingState.Idle or AppRecordingState.Completed or AppRecordingState.Error;
        }
    }

    public bool CanStop
    {
        get
        {
            lock (_lock)
                return _state == AppRecordingState.Recording;
        }
    }

    public bool TryTransition(AppRecordingState target, out AppRecordingState previous, out string? error)
    {
        lock (_lock)
        {
            previous = _state;
            if (!IsTransitionAllowed(_state, target))
            {
                error = $"Niedozwolone przejście ze stanu {_state} do {target}.";
                return false;
            }

            _state = target;
            error = null;
            return true;
        }
    }

    public void Force(AppRecordingState state)
    {
        lock (_lock)
            _state = state;
    }

    private static bool IsTransitionAllowed(AppRecordingState from, AppRecordingState to) => (from, to) switch
    {
        (AppRecordingState.Idle, AppRecordingState.Starting) => true,
        (AppRecordingState.Completed, AppRecordingState.Starting) => true,
        (AppRecordingState.Error, AppRecordingState.Starting) => true,
        (AppRecordingState.Starting, AppRecordingState.Recording) => true,
        (AppRecordingState.Starting, AppRecordingState.Error) => true,
        (AppRecordingState.Starting, AppRecordingState.Idle) => true,
        (AppRecordingState.Recording, AppRecordingState.Stopping) => true,
        (AppRecordingState.Recording, AppRecordingState.Error) => true,
        (AppRecordingState.Stopping, AppRecordingState.Processing) => true,
        (AppRecordingState.Stopping, AppRecordingState.Error) => true,
        (AppRecordingState.Processing, AppRecordingState.Completed) => true,
        (AppRecordingState.Processing, AppRecordingState.Error) => true,
        (AppRecordingState.Completed, AppRecordingState.Idle) => true,
        (AppRecordingState.Error, AppRecordingState.Idle) => true,
        _ => false
    };
}
