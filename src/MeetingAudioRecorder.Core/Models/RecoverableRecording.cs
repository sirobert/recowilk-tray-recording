namespace MeetingAudioRecorder.Core.Models;

/// <summary>
/// Niedokończone nagranie wykryte w katalogu Temp.
/// </summary>
public sealed class RecoverableRecording
{
    public Guid RecordingId { get; init; }
    public string MicrophoneTempPath { get; init; } = string.Empty;
    public string LoopbackTempPath { get; init; } = string.Empty;
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.Now;
    public long MicrophoneFileSize { get; init; }
    public long LoopbackFileSize { get; init; }
    public bool HasValidMicrophoneFile { get; init; }
    public bool HasValidLoopbackFile { get; init; }
}
