namespace MeetingAudioRecorder.Core.Services;

public static class RecordingTimeCalculator
{
    public static TimeSpan CalculateCaptureDuration(
        long stoppedAtTicks,
        long microphoneStartTicks,
        long loopbackStartTicks,
        TimeSpan fallback)
    {
        var captureStartedAtTicks = GetEarliestPositiveTimestamp(
            microphoneStartTicks,
            loopbackStartTicks);

        return captureStartedAtTicks > 0 && stoppedAtTicks >= captureStartedAtTicks
            ? TimeSpan.FromTicks(stoppedAtTicks - captureStartedAtTicks)
            : fallback;
    }

    private static long GetEarliestPositiveTimestamp(long first, long second)
    {
        if (first <= 0)
            return Math.Max(0, second);
        if (second <= 0)
            return first;
        return Math.Min(first, second);
    }
}
