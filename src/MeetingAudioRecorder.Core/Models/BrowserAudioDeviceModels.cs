namespace MeetingAudioRecorder.Core.Models;

public sealed record BrowserAudioSessionCandidate(
    string DeviceId,
    AudioDeviceType DeviceType,
    string ProcessName,
    bool IsActive,
    float PeakValue,
    bool IsDefaultCommunications,
    string? DeviceFriendlyName = null);

public sealed record BrowserAudioDeviceSelection(
    string? MicrophoneDeviceId,
    string? OutputDeviceId,
    string? BrowserProcessName,
    string? MicrophoneFriendlyName = null,
    string? OutputFriendlyName = null)
{
    public bool HasDetectedDevice => MicrophoneDeviceId is not null || OutputDeviceId is not null;
}

public sealed record RecordingDeviceSelection(
    string? MicrophoneDeviceId,
    string? OutputDeviceId,
    string? SelectionReason = null);
