using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Encoding;

/// <summary>
/// Wprowadza granicę anulowania pomiędzy kolejnymi odczytami wykonywanymi przez Media Foundation.
/// </summary>
public sealed class CancellationWaveProvider : IWaveProvider
{
    private readonly IWaveProvider _source;
    private readonly CancellationToken _cancellationToken;

    public CancellationWaveProvider(IWaveProvider source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _cancellationToken = cancellationToken;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(byte[] buffer, int offset, int count)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var read = _source.Read(buffer, offset, count);
        _cancellationToken.ThrowIfCancellationRequested();
        return read;
    }
}
