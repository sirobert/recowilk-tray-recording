using MeetingAudioRecorder.Audio.Encoding;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Tests;

public class MediaFoundationMp3EncodingIntegrationTests
{
    [WindowsIntegrationFact]
    [Trait("Category", "WindowsIntegration")]
    public async Task EncodeToMp3_RealMediaFoundation_CreatesReadableFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "mar-mf-integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var wavPath = Path.Combine(directory, "input.wav");
            var mp3Path = Path.Combine(directory, "output.mp3.partial");
            WriteSineWav(wavPath, duration: TimeSpan.FromSeconds(2));
            var sut = new Mp3EncodingService(NullLogger<Mp3EncodingService>.Instance);

            await sut.EncodeToMp3Async(wavPath, mp3Path, bitrateKbps: 128);

            Assert.True(new FileInfo(mp3Path).Length > 1024);
            using var reader = new Mp3FileReader(mp3Path);
            Assert.InRange(reader.TotalTime.TotalSeconds, 1.8, 2.2);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static void WriteSineWav(string path, TimeSpan duration)
    {
        const int sampleRate = 48000;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        using var writer = new WaveFileWriter(path, format);
        var frame = new float[2];

        for (var index = 0; index < duration.TotalSeconds * sampleRate; index++)
        {
            var sample = (float)(0.25 * Math.Sin(2 * Math.PI * 440 * index / sampleRate));
            frame[0] = sample;
            frame[1] = sample;
            writer.WriteSamples(frame, 0, frame.Length);
        }
    }
}
