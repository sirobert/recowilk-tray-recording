using MeetingAudioRecorder.TranslationWorker;
using Xunit;

public class ReplaySegmenterTests
{
    [Fact]
    public void SilenceDoesNotCreateUtterance()
    {
        var s = new ReplaySegmenter();
        for (int i = 0; i < 100; i++) Assert.Null(s.Push(0));
        Assert.Null(s.Flush());
    }
    [Fact]
    public void EndpointIncludesPreRollAndWaitsFor700ms()
    {
        var s = new ReplaySegmenter();
        for (int i = 0; i < 10; i++) s.Push(0);
        s.Push(.6f);
        for (int i = 0; i < 21; i++) Assert.Null(s.Push(.1f));
        var u = s.Push(.1f)!;
        Assert.Equal(5120 - 3200, u.StartSample);
        Assert.Equal(5632, u.LastSpeechEndSample);
        Assert.Equal(5632 + 22 * 512, u.EndSample);
        Assert.Equal("silence", u.Reason);
        Assert.Null(s.Flush());
    }
    [Fact]
    public void ContinuousSpeechIsSplitWithoutOverlapOrMissingSamples()
    {
        var s = new ReplaySegmenter(); var pieces = new List<ReplayUtterance>();
        for (int i = 0; i < 1300; i++) { var u = s.Push(.8f); if (u is not null) pieces.Add(u); }
        pieces.Add(s.Flush()!);
        Assert.Equal(3, pieces.Count);
        Assert.Equal(0, pieces[0].StartSample);
        Assert.Equal(1300 * 512, pieces[^1].EndSample);
        Assert.All(pieces, u => Assert.InRange(u.EndSample - u.StartSample, 1, 320000));
        for (int i = 1; i < pieces.Count; i++) Assert.Equal(pieces[i - 1].EndSample, pieces[i].StartSample);
    }
    [Fact]
    public void HysteresisAndPartialFinalFramePreserveEnd()
    {
        var s = new ReplaySegmenter(); s.Push(.6f); s.Push(.4f, 100);
        var u = s.Flush()!; Assert.Equal(612, u.EndSample); Assert.Equal(612, u.LastSpeechEndSample);
    }
    [Fact] public void InvalidProbabilityIsAnErrorNotSilence() => Assert.Throws<ArgumentOutOfRangeException>(() => new ReplaySegmenter().Push(float.NaN));
    [Fact]
    public void SilenceAfterExactMaximumDoesNotCreateAnotherUtterance()
    {
        var s = new ReplaySegmenter(); for (int i = 0; i < 624; i++) Assert.Null(s.Push(.8f));
        Assert.Equal("maximum", s.Push(.8f)!.Reason);
        for (int i = 0; i < 30; i++) Assert.Null(s.Push(0));
        Assert.Null(s.Flush());
    }
}
