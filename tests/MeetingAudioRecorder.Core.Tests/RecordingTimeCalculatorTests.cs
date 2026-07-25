using MeetingAudioRecorder.Core.Services;

namespace MeetingAudioRecorder.Core.Tests;

public class RecordingTimeCalculatorTests
{
    [Fact]
    public void UsesEarliestCaptureTimestampAsCommonOrigin()
    {
        var duration = RecordingTimeCalculator.CalculateCaptureDuration(
            stoppedAtTicks: 15 * TimeSpan.TicksPerSecond,
            microphoneStartTicks: 5 * TimeSpan.TicksPerSecond,
            loopbackStartTicks: 6 * TimeSpan.TicksPerSecond,
            fallback: TimeSpan.FromSeconds(8));

        Assert.Equal(TimeSpan.FromSeconds(10), duration);
    }

    [Fact]
    public void UsesAvailableTimestampWhenOneCaptureHasNoTimestamp()
    {
        var duration = RecordingTimeCalculator.CalculateCaptureDuration(
            stoppedAtTicks: 15 * TimeSpan.TicksPerSecond,
            microphoneStartTicks: 0,
            loopbackStartTicks: 6 * TimeSpan.TicksPerSecond,
            fallback: TimeSpan.FromSeconds(8));

        Assert.Equal(TimeSpan.FromSeconds(9), duration);
    }

    [Fact]
    public void FallsBackWhenMonotonicTimestampsAreUnavailable()
    {
        var fallback = TimeSpan.FromSeconds(8);

        var duration = RecordingTimeCalculator.CalculateCaptureDuration(
            stoppedAtTicks: 15 * TimeSpan.TicksPerSecond,
            microphoneStartTicks: 0,
            loopbackStartTicks: 0,
            fallback);

        Assert.Equal(fallback, duration);
    }
}
