namespace MeetingAudioRecorder.Core.Models;

public sealed class AudioLevelEventArgs : EventArgs
{
    public AudioLevelEventArgs(float peak, float rms)
    {
        Peak = peak;
        Rms = rms;
    }

    public float Peak { get; }
    public float Rms { get; }
}

public sealed class DeviceChangedEventArgs : EventArgs
{
    public DeviceChangedEventArgs(string deviceId, DeviceChangeKind kind, string? message = null)
    {
        DeviceId = deviceId;
        Kind = kind;
        Message = message;
    }

    public string DeviceId { get; }
    public DeviceChangeKind Kind { get; }
    public string? Message { get; }
}

public enum DeviceChangeKind
{
    Added,
    Removed,
    StateChanged,
    DefaultChanged,
    PropertyChanged
}

public sealed class RecordingStateChangedEventArgs : EventArgs
{
    public RecordingStateChangedEventArgs(AppRecordingState previous, AppRecordingState current, string? message = null)
    {
        Previous = previous;
        Current = current;
        Message = message;
    }

    public AppRecordingState Previous { get; }
    public AppRecordingState Current { get; }
    public string? Message { get; }
}
