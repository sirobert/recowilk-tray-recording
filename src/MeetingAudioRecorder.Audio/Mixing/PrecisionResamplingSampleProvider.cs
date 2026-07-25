using NAudio.Dsp;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Mixing;

/// <summary>
/// WDL resampler accepting a fractional effective input rate for smooth drift correction.
/// </summary>
public sealed class PrecisionResamplingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly WdlResampler _resampler;
    private readonly int _channels;

    public PrecisionResamplingSampleProvider(
        ISampleProvider source,
        double effectiveInputRate,
        int outputSampleRate)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (effectiveInputRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(effectiveInputRate));
        if (outputSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputSampleRate));

        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputSampleRate, _channels);

        _resampler = new WdlResampler();
        _resampler.SetMode(interp: true, filtercnt: 2, sinc: false, sinc_size: 0, sinc_interpsize: 0);
        _resampler.SetFeedMode(wantInputDriven: false);
        _resampler.SetRates(effectiveInputRate, outputSampleRate);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var outputFramesRequested = count / _channels;
        if (outputFramesRequested <= 0)
            return 0;

        var inputFramesRequired = _resampler.ResamplePrepare(
            outputFramesRequested,
            _channels,
            out var inputBuffer,
            out var inputBufferOffset);
        var inputSamplesRead = _source.Read(
            inputBuffer,
            inputBufferOffset,
            inputFramesRequired * _channels);
        var inputFramesRead = inputSamplesRead / _channels;
        var outputFrames = _resampler.ResampleOut(
            buffer,
            offset,
            inputFramesRead,
            outputFramesRequested,
            _channels);
        return outputFrames * _channels;
    }
}

