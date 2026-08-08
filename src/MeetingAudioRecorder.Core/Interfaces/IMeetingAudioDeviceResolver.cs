using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IMeetingAudioDeviceResolver
{
    BrowserAudioDeviceSelection DetectActiveBrowserDevices(
        string? savedMicrophoneDeviceId,
        string? savedOutputDeviceId);
}
