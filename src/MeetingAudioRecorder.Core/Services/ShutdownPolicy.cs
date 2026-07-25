using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Services;

public static class ShutdownPolicy
{
    public static ShutdownAction Decide(AppRecordingState state, UserExitChoice choice)
    {
        if (state is AppRecordingState.Starting or AppRecordingState.Stopping or AppRecordingState.Processing)
            return ShutdownAction.WaitForOperation;

        if (state != AppRecordingState.Recording)
            return ShutdownAction.ShutdownNow;

        return choice switch
        {
            UserExitChoice.SaveRecording => ShutdownAction.StopAndSave,
            UserExitChoice.PreserveTemporaryFiles => ShutdownAction.ShutdownNow,
            _ => ShutdownAction.Cancel
        };
    }
}
