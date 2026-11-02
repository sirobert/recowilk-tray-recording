using MeetingAudioRecorder.TranslationWorker;
using Xunit;
public class SampleRingTests
{
    [Fact]
    public void WrappedRangeKeepsAbsoluteOrder()
    {
        var ring = new SampleRing(5); ring.Append([1, 2, 3, 4]); ring.Append([5, 6, 7]);
        Assert.Equal(new float[] { 3, 4, 5, 6, 7 }, ring.Slice(2, 7));
        Assert.Equal(7, ring.Position);
    }
    [Fact]
    public void ExpiredAndFutureAudioCannotBeSilentlyRead()
    {
        var ring = new SampleRing(3); ring.Append([1, 2, 3, 4]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.Slice(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.Slice(1, 5));
    }
    [Fact]
    public void QueuedAudioIsIndependentOfLaterCapture()
    {
        var ring = new SampleRing(3); ring.Append([1, 2, 3]); var copy = ring.Slice(0, 3); ring.Append([4, 5, 6]);
        Assert.Equal(new float[] { 1, 2, 3 }, copy);
    }
}
