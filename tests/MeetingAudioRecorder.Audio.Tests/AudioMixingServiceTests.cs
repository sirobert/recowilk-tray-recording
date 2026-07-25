using MeetingAudioRecorder.Audio.Mixing;
using MeetingAudioRecorder.Core.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Tests;

public class AudioMixingServiceTests
{
    [Fact]
    public async Task MixToWav_TwoSineWaves_CreatesStereoFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mar-mix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var micPath = Path.Combine(dir, "mic.wav");
            var loopPath = Path.Combine(dir, "loop.wav");
            var outPath = Path.Combine(dir, "mixed.wav");

            WriteSineWav(micPath, 440, TimeSpan.FromMilliseconds(200), sampleRate: 44100, channels: 1);
            WriteSineWav(loopPath, 880, TimeSpan.FromMilliseconds(200), sampleRate: 48000, channels: 2);

            var sut = new AudioMixingService(NullLogger<AudioMixingService>.Instance);
            await sut.MixToWavAsync(new MixRequest
            {
                MicrophoneWavPath = micPath,
                LoopbackWavPath = loopPath,
                OutputWavPath = outPath,
                TargetSampleRate = 48000,
                MicrophoneVolume = 1.0,
                LoopbackVolume = 0.85,
                MicrophoneStartOffsetTicks = 0,
                LoopbackStartOffsetTicks = TimeSpan.FromMilliseconds(20).Ticks
            });

            Assert.True(File.Exists(outPath));
            Assert.True(new FileInfo(outPath).Length > 1000);

            using var reader = new AudioFileReader(outPath);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.Equal(48000, reader.WaveFormat.SampleRate);
            Assert.True(reader.TotalTime.TotalMilliseconds >= 180);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task MixToWav_MissingMic_StillProducesOutput()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mar-mix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var loopPath = Path.Combine(dir, "loop.wav");
            var outPath = Path.Combine(dir, "mixed.wav");
            WriteSineWav(loopPath, 440, TimeSpan.FromMilliseconds(100), 48000, 2);

            var sut = new AudioMixingService(NullLogger<AudioMixingService>.Instance);
            await sut.MixToWavAsync(new MixRequest
            {
                MicrophoneWavPath = Path.Combine(dir, "missing.wav"),
                LoopbackWavPath = loopPath,
                OutputWavPath = outPath,
                TargetSampleRate = 48000
            });

            Assert.True(File.Exists(outPath));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    private static void WriteSineWav(string path, double freq, TimeSpan duration, int sampleRate, int channels)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        using var writer = new WaveFileWriter(path, format);
        var samples = (int)(duration.TotalSeconds * sampleRate);
        var buffer = new float[channels];
        for (var n = 0; n < samples; n++)
        {
            var t = n / (double)sampleRate;
            var s = (float)(0.4 * Math.Sin(2 * Math.PI * freq * t));
            for (var c = 0; c < channels; c++)
                buffer[c] = s;
            writer.WriteSamples(buffer, 0, channels);
        }
    }
}
