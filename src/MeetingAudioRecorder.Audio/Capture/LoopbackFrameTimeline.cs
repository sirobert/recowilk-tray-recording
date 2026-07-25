namespace MeetingAudioRecorder.Audio.Capture;

/// <summary>
/// Plans placement of loopback packets on a monotonic, frame-based timeline.
/// It never commits speculative silence before the next packet confirms a gap.
/// </summary>
public sealed class LoopbackFrameTimeline
{
    private readonly int _sampleRate;
    private readonly long _gapToleranceFrames;

    public LoopbackFrameTimeline(int sampleRate, long gapToleranceFrames)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (gapToleranceFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(gapToleranceFrames));

        _sampleRate = sampleRate;
        _gapToleranceFrames = gapToleranceFrames;
    }

    public long PositionFrames { get; private set; }

    public LoopbackWritePlan PlanPacket(long elapsedTicks, int audioFrames)
    {
        if (elapsedTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedTicks));
        if (audioFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(audioFrames));

        var elapsedFrames = ToFrames(elapsedTicks);
        var packetStartFrame = Math.Max(0, elapsedFrames - audioFrames);
        var gapFrames = packetStartFrame - PositionFrames;
        var silenceFrames = gapFrames > _gapToleranceFrames ? gapFrames : 0;

        PositionFrames = checked(PositionFrames + silenceFrames + audioFrames);
        return new LoopbackWritePlan(silenceFrames, audioFrames);
    }

    public long PlanCompletion(long elapsedTicks)
    {
        if (elapsedTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedTicks));

        var elapsedFrames = ToFrames(elapsedTicks);
        var silenceFrames = Math.Max(0, elapsedFrames - PositionFrames);
        PositionFrames = checked(PositionFrames + silenceFrames);
        return silenceFrames;
    }

    private long ToFrames(long elapsedTicks)
        => checked(elapsedTicks * _sampleRate / TimeSpan.TicksPerSecond);
}

public readonly record struct LoopbackWritePlan(long SilenceFrames, int AudioFrames);

