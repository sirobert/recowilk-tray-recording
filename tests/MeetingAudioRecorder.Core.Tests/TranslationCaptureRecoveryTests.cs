using System.Runtime.InteropServices;
using MeetingAudioRecorder.TranslationWorker;

namespace MeetingAudioRecorder.Core.Tests;

public class TranslationCaptureRecoveryTests
{
    private static Exception Invalidated() => new COMException("0x88890004", unchecked((int)0x88890004));

    [Theory]
    [InlineData(true, "Wyjście audio")]
    [InlineData(false, "Mikrofon")]
    public void ErrorIdentifiesSourceAndRecoveryAction(bool output, string source)
    {
        var message = CaptureStartupRecovery.Describe(new InvalidOperationException("capture", Invalidated()), output);
        Assert.Contains(source, message); Assert.Contains("0x88890004", message);
        Assert.Contains("uruchom tłumaczenie ponownie", message);
    }

    [Fact]
    public async Task ReopensAfterTransientInvalidationBeforeFirstPacket()
    {
        int calls = 0, retries = 0;
        var result = await CaptureStartupRecovery.Run(() => ++calls == 1
            ? Task.FromException<int>(new InvalidOperationException("capture", Invalidated()))
            : Task.FromResult(42), () => false, () => retries++, CancellationToken.None);
        Assert.Equal(42, result); Assert.Equal(2, calls); Assert.Equal(1, retries);
    }

    [Fact]
    public async Task PersistentInvalidationHasBoundedRetries()
    {
        int calls = 0;
        await Assert.ThrowsAsync<COMException>(() => CaptureStartupRecovery.Run<int>(() =>
        {
            calls++; throw Invalidated();
        }, () => false, () => { }, CancellationToken.None));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task NeverJoinsAudioAcrossDeviceDisconnection()
    {
        int calls = 0;
        await Assert.ThrowsAsync<COMException>(() => CaptureStartupRecovery.Run<int>(() =>
        {
            calls++; throw Invalidated();
        }, () => true, () => Assert.Fail("Must not retry after audio"), CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnrelatedFailureIsNotRetried()
    {
        await Assert.ThrowsAsync<IOException>(() => CaptureStartupRecovery.Run<int>(() => throw new IOException("failure"),
            () => false, () => Assert.Fail("Must not retry other errors"), CancellationToken.None));
    }

    [Fact]
    public async Task StopCancelsRetryDelay()
    {
        using var stop = new CancellationTokenSource(); int calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CaptureStartupRecovery.Run<int>(() =>
        {
            calls++; throw Invalidated();
        }, () => false, stop.Cancel, stop.Token));
        Assert.Equal(1, calls);
    }
}
