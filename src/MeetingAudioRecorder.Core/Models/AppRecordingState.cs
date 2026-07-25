namespace MeetingAudioRecorder.Core.Models;

/// <summary>
/// Model stanu aplikacji nagrywającej.
/// </summary>
public enum AppRecordingState
{
    Idle = 0,
    Starting = 1,
    Recording = 2,
    Stopping = 3,
    Processing = 4,
    Completed = 5,
    Error = 6
}
