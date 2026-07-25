using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;

namespace MeetingAudioRecorder.Core.Tests;

public class ShutdownPolicyTests
{
    [Theory]
    [InlineData(UserExitChoice.SaveRecording, ShutdownAction.StopAndSave)]
    [InlineData(UserExitChoice.PreserveTemporaryFiles, ShutdownAction.ShutdownNow)]
    [InlineData(UserExitChoice.Cancel, ShutdownAction.Cancel)]
    public void Recording_RespectsExplicitUserChoice(UserExitChoice choice, ShutdownAction expected)
    {
        Assert.Equal(expected, ShutdownPolicy.Decide(AppRecordingState.Recording, choice));
    }

    [Theory]
    [InlineData(AppRecordingState.Starting)]
    [InlineData(AppRecordingState.Stopping)]
    [InlineData(AppRecordingState.Processing)]
    public void OperationInProgress_DelaysShutdown(AppRecordingState state)
    {
        Assert.Equal(ShutdownAction.WaitForOperation, ShutdownPolicy.Decide(state, UserExitChoice.Cancel));
    }

    [Theory]
    [InlineData(AppRecordingState.Idle)]
    [InlineData(AppRecordingState.Completed)]
    [InlineData(AppRecordingState.Error)]
    public void StableState_ShutsDownImmediately(AppRecordingState state)
    {
        Assert.Equal(ShutdownAction.ShutdownNow, ShutdownPolicy.Decide(state, UserExitChoice.Cancel));
    }
}
