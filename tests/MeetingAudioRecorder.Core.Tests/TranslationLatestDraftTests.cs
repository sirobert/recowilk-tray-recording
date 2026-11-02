using MeetingAudioRecorder.TranslationWorker;
using Xunit;

public class LatestDraftTests
{
    [Fact]
    public void KeepsOnlyLatestPendingSnapshot()
    {
        var d = new LatestDraft<string>(); d.Offer(1, "a"); d.Offer(1, "ab");
        Assert.Equal("ab", d.Take()); Assert.Null(d.Take());
    }
    [Fact]
    public void ClosingInvalidatesPendingAndInFlightButPreservesNextUtterance()
    {
        var d = new LatestDraft<string>(); d.Offer(1, "a"); Assert.Equal("a", d.Take());
        d.Offer(2, "b"); d.CloseThrough(1);
        Assert.False(d.Publish(1, () => throw new Exception("Late draft")));
        d.Offer(1, "stale"); Assert.Equal("b", d.Take()); Assert.True(d.IsOpen(2));
        d.CloseThrough(2); Assert.False(d.IsOpen(2));
    }
    [Fact]
    public void SnapshotDoesNotCommitAndRetainsFullGrowingContext()
    {
        var s = new ReplaySegmenter(); Assert.Null(s.Snapshot());
        for (int i = 0; i < 60; i++) s.Push(.8f);
        var a = s.Snapshot()!; s.Push(.8f); var b = s.Snapshot()!;
        Assert.Equal(a.StartSample, b.StartSample); Assert.Equal(a.EndSample + 512, b.EndSample);
        var final = s.Flush()!; Assert.Equal(b.StartSample, final.StartSample); Assert.Equal(b.EndSample, final.EndSample);
        Assert.Null(s.Snapshot()); s.Push(.8f); Assert.Equal(final.EndSample, s.Snapshot()!.StartSample);
    }
}
