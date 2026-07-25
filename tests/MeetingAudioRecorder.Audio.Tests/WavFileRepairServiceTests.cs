using System.Text;
using MeetingAudioRecorder.Infrastructure.Recovery;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Tests;

public class WavFileRepairServiceTests
{
    [Fact]
    public void RepairToCopy_ZeroedCrashHeader_RecoversAllCompleteFrames()
    {
        using var fixture = new WavFixture();
        var source = fixture.CreateInterruptedWav(durationMs: 250, appendPartialFrame: true);
        var sourceBefore = File.ReadAllBytes(source);
        var destination = Path.Combine(fixture.DirectoryPath, "recovered.wav");

        var sut = new WavFileRepairService();
        var result = sut.RepairToCopy(source, destination);

        Assert.True(result.Success);
        Assert.Equal(sourceBefore, File.ReadAllBytes(source));
        Assert.Equal(0, (result.DataLengthBytes % result.BlockAlign));

        using var reader = new WaveFileReader(destination);
        Assert.Equal(48000, reader.WaveFormat.SampleRate);
        Assert.Equal(2, reader.WaveFormat.Channels);
        Assert.InRange(reader.TotalTime.TotalMilliseconds, 249, 251);
    }

    [Fact]
    public void RepairToCopy_RepairedInput_IsIdempotent()
    {
        using var fixture = new WavFixture();
        var source = fixture.CreateInterruptedWav(durationMs: 100, appendPartialFrame: false);
        var first = Path.Combine(fixture.DirectoryPath, "first.wav");
        var second = Path.Combine(fixture.DirectoryPath, "second.wav");
        var sut = new WavFileRepairService();

        var firstResult = sut.RepairToCopy(source, first);
        var secondResult = sut.RepairToCopy(first, second);

        Assert.True(firstResult.Success);
        Assert.True(secondResult.Success);
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
    }

    [Fact]
    public void CanRecover_RejectsInvalidOrHeaderOnlyFiles()
    {
        using var fixture = new WavFixture();
        var invalid = Path.Combine(fixture.DirectoryPath, "invalid.wav");
        File.WriteAllBytes(invalid, "not a wav"u8.ToArray());

        var sut = new WavFileRepairService();

        Assert.False(sut.CanRecover(invalid));
    }

    private sealed class WavFixture : IDisposable
    {
        public WavFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "mar-wav-repair-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string CreateInterruptedWav(int durationMs, bool appendPartialFrame)
        {
            var path = Path.Combine(DirectoryPath, "interrupted.wav");
            var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            using (var writer = new WaveFileWriter(path, format))
            {
                var frames = 48000 * durationMs / 1000;
                writer.Write(new byte[frames * format.BlockAlign], 0, frames * format.BlockAlign);
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                var header = new byte[Math.Min(stream.Length, 512)];
                _ = stream.Read(header, 0, header.Length);
                var dataChunkOffset = header.AsSpan().IndexOf("data"u8);
                if (dataChunkOffset < 0)
                    throw new InvalidDataException("Test fixture does not contain a data chunk.");

                stream.Position = 4;
                writer.Write(0u);
                stream.Position = dataChunkOffset + 4;
                writer.Write(0u);
                if (appendPartialFrame)
                {
                    stream.Position = stream.Length;
                    writer.Write((byte)0x7f);
                }
            }

            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(DirectoryPath, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
