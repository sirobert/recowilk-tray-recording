using MeetingAudioRecorder.Core.Interfaces;

namespace MeetingAudioRecorder.Core.Models;

public sealed class RecordingSessionManifest
{
    public int Version { get; init; } = 1;
    public Guid RecordingId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? StoppedAt { get; set; }
    public string State { get; set; } = "recording";
    public string MicrophoneDeviceId { get; init; } = string.Empty;
    public string OutputDeviceId { get; init; } = string.Empty;
    public string MicrophoneTempPath { get; init; } = string.Empty;
    public string LoopbackTempPath { get; init; } = string.Empty;
    public WaveFormatInfo? MicrophoneFormat { get; set; }
    public WaveFormatInfo? LoopbackFormat { get; set; }
    public long MicrophoneStartOffsetTicks { get; set; }
    public long LoopbackStartOffsetTicks { get; set; }
    public long? DurationTicks { get; set; }
}
