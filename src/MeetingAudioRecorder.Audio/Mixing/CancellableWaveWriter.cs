using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingAudioRecorder.Audio.Mixing;

public static class CancellableWaveWriter
{
    public static long WriteTo16BitWav(
        string path,
        ISampleProvider source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var waveProvider = new SampleToWaveProvider16(source);
        using var writer = new WaveFileWriter(path, waveProvider.WaveFormat);
        var buffer = new byte[waveProvider.WaveFormat.AverageBytesPerSecond];
        long framesWritten = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = waveProvider.Read(buffer, 0, buffer.Length);
            cancellationToken.ThrowIfCancellationRequested();
            if (read <= 0)
                break;

            writer.Write(buffer, 0, read);
            framesWritten += read / waveProvider.WaveFormat.BlockAlign;
        }

        return framesWritten;
    }
}
