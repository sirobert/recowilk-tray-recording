using MeetingAudioRecorder.Audio.Mixing;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Tests;

public class PrecisionResamplingSampleProviderTests
{
    [Fact]
    public void FractionalInputRate_SmoothlyCorrectsOutputFrameCount()
    {
        const int nominalRate = 48_000;
        const int sourceFrames = 48_048;
        var source = new FiniteSilenceSampleProvider(nominalRate, channels: 2, sourceFrames);
        var resampler = new PrecisionResamplingSampleProvider(
            source,
            effectiveInputRate: 48_048,
            outputSampleRate: nominalRate);
        var buffer = new float[4096];
        long outputSamples = 0;

        int read;
        while ((read = resampler.Read(buffer, 0, buffer.Length)) > 0)
            outputSamples += read;

        var outputFrames = outputSamples / 2;
        Assert.InRange(outputFrames, nominalRate - 2, nominalRate + 2);
    }

    private sealed class FiniteSilenceSampleProvider : ISampleProvider
    {
        private long _remainingSamples;

        public FiniteSilenceSampleProvider(int sampleRate, int channels, long frames)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            _remainingSamples = frames * channels;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, _remainingSamples);
            Array.Clear(buffer, offset, read);
            _remainingSamples -= read;
            return read;
        }
    }
}
