namespace MeetingAudioRecorder.Core.Services;

/// <summary>
/// Operacje na próbkach float: miks, cisza, clipping.
/// </summary>
public static class AudioMath
{
    /// <summary>
    /// Oblicza opóźnienie startu źródła względem wspólnego t0 (w tickach Stopwatch / TimeSpan).
    /// Zwraca liczbę próbek ciszy do wstawienia na początku.
    /// </summary>
    public static int CalculateLeadingSilenceSamples(long sourceStartOffsetTicks, long referenceStartOffsetTicks, int sampleRate, int channels)
    {
        var delayTicks = sourceStartOffsetTicks - referenceStartOffsetTicks;
        if (delayTicks <= 0)
            return 0;

        var delaySeconds = delayTicks / (double)TimeSpan.TicksPerSecond;
        var frames = (int)Math.Round(delaySeconds * sampleRate);
        return Math.Max(0, frames * channels);
    }

    /// <summary>
    /// Miksuje dwie próbki float z wagami głośności i ogranicza clipping.
    /// </summary>
    public static float MixSamples(float a, float b, float volumeA, float volumeB)
    {
        var mixed = (a * volumeA) + (b * volumeB);
        return SoftLimit(mixed);
    }

    /// <summary>
    /// Miękki limiter (tanh-like) zapobiegający twardej saturacji.
    /// </summary>
    public static float SoftLimit(float sample)
    {
        // Soft knee: poniżej progu bez zmian, powyżej delikatne ściśnięcie
        const float threshold = 0.95f;
        var abs = Math.Abs(sample);
        if (abs <= threshold)
            return sample;

        // asymptotic approach to 1.0
        var sign = Math.Sign(sample);
        var excess = abs - threshold;
        var limited = threshold + (excess / (1f + excess * 4f));
        return sign * Math.Min(limited, 0.999f);
    }

    /// <summary>
    /// Twarde ograniczenie do [-1, 1].
    /// </summary>
    public static float HardClip(float sample) => Math.Clamp(sample, -1f, 1f);

    public static void MixBuffers(ReadOnlySpan<float> mic, ReadOnlySpan<float> loopback, Span<float> output, float micVolume, float loopbackVolume)
    {
        var len = Math.Min(Math.Min(mic.Length, loopback.Length), output.Length);
        for (var i = 0; i < len; i++)
            output[i] = MixSamples(mic[i], loopback[i], micVolume, loopbackVolume);
    }

    public static float CalculatePeak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var s in samples)
        {
            var a = Math.Abs(s);
            if (a > peak) peak = a;
        }
        return peak;
    }

    public static float CalculateRms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0f;
        double sum = 0;
        foreach (var s in samples)
            sum += s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    public static float[] CreateSilence(int sampleCount) => new float[sampleCount];
}
