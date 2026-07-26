using MeetingAudioRecorder.Audio.Encoding;
using MeetingAudioRecorder.Audio.Mixing;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Tests;

public class CancellationBoundaryTests
{
    [Fact]
    public void WavWriter_ReturnsExactWrittenFrameCount()
    {
        const int expectedFrames = 4800;
        var path = Path.Combine(Path.GetTempPath(), "mar-frames-" + Guid.NewGuid().ToString("N") + ".wav");
        var source = new FiniteSampleProvider(expectedFrames);

        try
        {
            var frames = CancellableWaveWriter.WriteTo16BitWav(path, source);

            Assert.Equal(expectedFrames, frames);
            using var reader = new WaveFileReader(path);
            Assert.Equal(expectedFrames, reader.Length / reader.WaveFormat.BlockAlign);
        }
        finally
        {
            try { File.Delete(path); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public void WavWriter_CancellationDuringSourceRead_StopsBeforeWritingBuffer()
    {
        var path = Path.Combine(Path.GetTempPath(), "mar-cancel-" + Guid.NewGuid().ToString("N") + ".wav");
        using var cts = new CancellationTokenSource();
        var source = new CancellingSampleProvider(cts);

        try
        {
            Assert.Throws<OperationCanceledException>(
                () => CancellableWaveWriter.WriteTo16BitWav(path, source, cts.Token));

            Assert.Equal(1, source.ReadCount);
            Assert.True(File.Exists(path));
            Assert.InRange(new FileInfo(path).Length, 44, 128);
        }
        finally
        {
            try { File.Delete(path); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public void WaveProvider_CancellationDuringRead_StopsMediaFoundationInput()
    {
        using var cts = new CancellationTokenSource();
        var source = new CancellingWaveProvider(cts);
        var sut = new CancellationWaveProvider(source, cts.Token);

        Assert.Throws<OperationCanceledException>(() => sut.Read(new byte[1024], 0, 1024));
        Assert.Equal(1, source.ReadCount);
    }

    private sealed class CancellingSampleProvider : ISampleProvider
    {
        private readonly CancellationTokenSource _cts;

        public CancellingSampleProvider(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public int ReadCount { get; private set; }
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            ReadCount++;
            Array.Clear(buffer, offset, count);
            _cts.Cancel();
            return count;
        }
    }

    private sealed class CancellingWaveProvider : IWaveProvider
    {
        private readonly CancellationTokenSource _cts;

        public CancellingWaveProvider(CancellationTokenSource cts)
        {
            _cts = cts;
        }

        public int ReadCount { get; private set; }
        public WaveFormat WaveFormat { get; } = new(48000, 16, 2);

        public int Read(byte[] buffer, int offset, int count)
        {
            ReadCount++;
            Array.Clear(buffer, offset, count);
            _cts.Cancel();
            return count;
        }
    }

    private sealed class FiniteSampleProvider : ISampleProvider
    {
        private int _remainingSamples;

        public FiniteSampleProvider(int frames)
        {
            _remainingSamples = frames * WaveFormat.Channels;
        }

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            var read = Math.Min(count, _remainingSamples);
            Array.Clear(buffer, offset, read);
            _remainingSamples -= read;
            return read;
        }
    }
}
