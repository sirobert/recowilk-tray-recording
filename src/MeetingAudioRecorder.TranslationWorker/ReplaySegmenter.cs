namespace MeetingAudioRecorder.TranslationWorker;

public sealed record ReplayUtterance(long StartSample, long EndSample, long LastSpeechEndSample, string Reason);

// Sample-clock segmenter with non-destructive snapshots; no ASR, wall clock or model dependencies.
public sealed class ReplaySegmenter
{
    public const int SampleRate = 16000;
    public const int FrameSize = 512;
    const int PreRoll = 3200, Silence = 11200, Maximum = 320000;
    long position, start, lastSpeech, committedEnd;
    bool active, continuation;
    public ReplayUtterance? Push(float probability, int validSamples = FrameSize)
    {
        if (!float.IsFinite(probability) || probability < 0 || probability > 1) throw new ArgumentOutOfRangeException(nameof(probability));
        if (validSamples is < 1 or > FrameSize) throw new ArgumentOutOfRangeException(nameof(validSamples));
        long frameStart = position;
        position += validSamples;
        if (!active && probability >= (continuation ? .35f : .5f))
        {
            active = true;
            start = Math.Max(committedEnd, Math.Max(0, frameStart - PreRoll));
            lastSpeech = position;
        }
        continuation = false;
        if (!active) return null;
        if (probability >= .35f) lastSpeech = position;
        // Split on sample boundary at exactly 20s; next start retains the frame remainder.
        if (position - start >= Maximum)
        {
            var result = new ReplayUtterance(start, start + Maximum, Math.Min(lastSpeech, start + Maximum), "maximum");
            committedEnd = start + Maximum;
            start = committedEnd;
            active = position > start && lastSpeech > start;
            continuation = !active && probability >= .35f;
            return result;
        }
        if (position - lastSpeech >= Silence) return Finish("silence");
        return null;
    }
    public ReplayUtterance? Snapshot() => active && position > start ? new(start, position, lastSpeech, "draft") : null;
    public ReplayUtterance? Flush() => active && position > start ? Finish("eof") : null;
    ReplayUtterance Finish(string reason)
    {
        var result = new ReplayUtterance(start, position, lastSpeech, reason);
        active = false;
        committedEnd = position;
        return result;
    }
}
