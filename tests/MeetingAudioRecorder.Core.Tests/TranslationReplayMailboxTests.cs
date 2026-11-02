using MeetingAudioRecorder.TranslationWorker;
using Xunit;

public class ReplayMailboxTests
{
    [Fact]
    public void FourthWaitingCommitFailsInsteadOfDroppingOldest()
    {
        var q = new ReplayMailbox<string>(); for (int i = 0; i < 3; i++) Assert.True(q.Enqueue(i.ToString(), 0));
        Assert.False(q.Enqueue("four", 0)); Assert.Equal("queue-capacity", q.Fault); Assert.Equal(4, q.Discarded); Assert.Null(q.Take(0));
    }
    [Fact]
    public void WatchdogInvalidatesLateResultWithoutWaitingForWorker()
    {
        var q = new ReplayMailbox<string>(); q.Enqueue("active", 0); Assert.Equal("active", q.Take(0)); q.Enqueue("waiting", 1);
        q.CheckAge(2002); bool published = false; Assert.False(q.Publish(() => published = true)); Assert.False(published); Assert.Equal("queue-age", q.Fault);
    }
    [Fact]
    public void CompletionDrainsInOrder()
    {
        var q = new ReplayMailbox<string>(); q.Enqueue("a", 0); q.Enqueue("b", 1); q.Complete(); Assert.False(q.Finished);
        Assert.Equal("a", q.Take(2)); Assert.Equal("b", q.Take(3)); Assert.True(q.Finished); Assert.Null(q.Fault);
    }
    [Fact]
    public void ActiveWorkDoesNotCountAsWaiting()
    {
        var q = new ReplayMailbox<string>(); q.Enqueue("active", 0); q.Take(0);
        for (int i = 0; i < 3; i++) Assert.True(q.Enqueue("pending", 1)); Assert.Null(q.Fault);
    }
}
