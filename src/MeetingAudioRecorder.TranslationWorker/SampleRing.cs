namespace MeetingAudioRecorder.TranslationWorker;

// Absolute sample positions; bounded storage, never silently return overwritten audio.
public sealed class SampleRing(int capacity)
{
    readonly float[] samples = new float[capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];
    public long Position { get; private set; }
    public void Append(ReadOnlySpan<float> values)
    {
        foreach (float value in values) samples[(int)(Position++ % samples.Length)] = value;
    }
    public float[] Slice(long start, long end)
    {
        if (start < Math.Max(0, Position - samples.Length) || end > Position || end <= start)
            throw new ArgumentOutOfRangeException(nameof(start), "Audio range unavailable");
        var result = new float[checked((int)(end - start))];
        for (int i = 0; i < result.Length; i++) result[i] = samples[(int)((start + i) % samples.Length)];
        return result;
    }
}
