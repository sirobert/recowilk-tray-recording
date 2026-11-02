using MeetingAudioRecorder.TranslationWorker;
namespace MeetingAudioRecorder.Core.Tests;

public class TranslationIdleTests
{
    [Fact]
    public void IdleBoundaryFiresOnceAndNeverCreatesAudioSamples()
    {
        var idle = new CaptureIdleBoundary(); Assert.False(idle.TryClose(10000));
        idle.Packet(100); Assert.False(idle.TryClose(799)); Assert.True(idle.TryClose(800)); Assert.False(idle.TryClose(900));
        idle.Packet(1000); Assert.False(idle.TryClose(1699)); Assert.True(idle.TryClose(1700));
        var segmenter = new ReplaySegmenter(); segmenter.Push(.8f, 100);
        var utterance = segmenter.Flush()!; Assert.Equal(100, utterance.EndSample); Assert.Null(segmenter.Flush());
        segmenter.Push(.8f, 80); Assert.Equal(100, segmenter.Snapshot()!.StartSample); Assert.Equal(180, segmenter.Snapshot()!.EndSample);
    }
}
