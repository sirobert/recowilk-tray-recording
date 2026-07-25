using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;

namespace MeetingAudioRecorder.Core.Tests;

public class RecordingStateMachineTests
{
    [Fact]
    public void Initial_IsIdle()
    {
        var sm = new RecordingStateMachine();
        Assert.Equal(AppRecordingState.Idle, sm.State);
        Assert.True(sm.CanStart);
        Assert.False(sm.CanStop);
    }

    [Fact]
    public void HappyPath_Transitions()
    {
        var sm = new RecordingStateMachine();
        Assert.True(sm.TryTransition(AppRecordingState.Starting, out _, out _));
        Assert.True(sm.TryTransition(AppRecordingState.Recording, out _, out _));
        Assert.True(sm.CanStop);
        Assert.False(sm.CanStart);
        Assert.True(sm.TryTransition(AppRecordingState.Stopping, out _, out _));
        Assert.True(sm.TryTransition(AppRecordingState.Processing, out _, out _));
        Assert.True(sm.TryTransition(AppRecordingState.Completed, out _, out _));
        Assert.True(sm.CanStart);
    }

    [Fact]
    public void DoubleStart_Blocked()
    {
        var sm = new RecordingStateMachine();
        Assert.True(sm.TryTransition(AppRecordingState.Starting, out _, out _));
        Assert.False(sm.TryTransition(AppRecordingState.Starting, out _, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void DoubleStop_Blocked()
    {
        var sm = new RecordingStateMachine();
        sm.TryTransition(AppRecordingState.Starting, out _, out _);
        sm.TryTransition(AppRecordingState.Recording, out _, out _);
        Assert.True(sm.TryTransition(AppRecordingState.Stopping, out _, out _));
        Assert.False(sm.TryTransition(AppRecordingState.Stopping, out _, out _));
    }

    [Fact]
    public void StopFromIdle_Blocked()
    {
        var sm = new RecordingStateMachine();
        Assert.False(sm.TryTransition(AppRecordingState.Stopping, out _, out _));
    }

    [Fact]
    public void ErrorFromRecording_Allowed()
    {
        var sm = new RecordingStateMachine();
        sm.TryTransition(AppRecordingState.Starting, out _, out _);
        sm.TryTransition(AppRecordingState.Recording, out _, out _);
        Assert.True(sm.TryTransition(AppRecordingState.Error, out _, out _));
        Assert.True(sm.CanStart);
    }
}
