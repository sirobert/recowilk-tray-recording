using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Services;

public static class RecordingDeviceSelectionResolver
{
    public static AppSettings Apply(AppSettings settings, RecordingDeviceSelection? selection)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var effective = settings.Clone();
        if (!string.IsNullOrWhiteSpace(selection?.MicrophoneDeviceId))
            effective.MicrophoneDeviceId = selection.MicrophoneDeviceId;
        if (!string.IsNullOrWhiteSpace(selection?.OutputDeviceId))
            effective.OutputDeviceId = selection.OutputDeviceId;
        return effective;
    }
}
