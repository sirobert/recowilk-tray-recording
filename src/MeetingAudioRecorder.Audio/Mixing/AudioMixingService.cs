using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Services;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingAudioRecorder.Audio.Mixing;

/// <summary>
/// Resampling, wyrównanie startu, miks stereo, ochrona przed clippingiem.
/// </summary>
public sealed class AudioMixingService : IAudioMixingService
{
    private readonly ILogger<AudioMixingService> _logger;

    public AudioMixingService(ILogger<AudioMixingService> logger)
    {
        _logger = logger;
    }

    public Task MixToWavAsync(MixRequest request, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            MixInternal(request, cancellationToken);
        }, cancellationToken);
    }

    private void MixInternal(MixRequest request, CancellationToken cancellationToken)
    {
        var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(request.TargetSampleRate, 2);
        _logger.LogInformation(
            "Miks: mic={Mic}, loop={Loop}, target={Rate}Hz stereo",
            request.MicrophoneWavPath, request.LoopbackWavPath, request.TargetSampleRate);

        using var micReader = OpenReaderOrSilence(request.MicrophoneWavPath, targetFormat);
        using var loopReader = OpenReaderOrSilence(request.LoopbackWavPath, targetFormat);

        var micProvider = BuildPipeline(micReader, targetFormat, request.MicrophoneVolume,
            request.MicrophoneStartOffsetTicks, request.LoopbackStartOffsetTicks,
            request.ExpectedDurationTicks, request.TargetSampleRate, "mikrofon");

        var loopProvider = BuildPipeline(loopReader, targetFormat, request.LoopbackVolume,
            request.LoopbackStartOffsetTicks, request.MicrophoneStartOffsetTicks,
            request.ExpectedDurationTicks, request.TargetSampleRate, "loopback");

        // ReadFully=false — mikser kończy się, gdy oba źródła się wyczerpią (nie generuje nieskończonej ciszy)
        var mixer = new MixingSampleProvider(targetFormat) { ReadFully = false };
        mixer.AddMixerInput(new SoftLimiterSampleProvider(micProvider));
        mixer.AddMixerInput(new SoftLimiterSampleProvider(loopProvider));

        // Ogranicznik na sumie + zapis ręczny (bezpieczniejszy niż CreateWaveFile16 przy długich plikach)
        ISampleProvider limited = new SoftLimiterSampleProvider(mixer);

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputWavPath)!);
        WriteTo16BitWav(request.OutputWavPath, limited);

        if (request.KeepSeparateTracks)
        {
            if (!string.IsNullOrEmpty(request.SeparateMicrophoneOutputPath))
            {
                using var r = OpenReaderOrSilence(request.MicrophoneWavPath, targetFormat);
                var p = BuildPipeline(r, targetFormat, 1.0,
                    request.MicrophoneStartOffsetTicks, request.LoopbackStartOffsetTicks,
                    request.ExpectedDurationTicks, request.TargetSampleRate, "mikrofon-osobno");
                WriteTo16BitWav(request.SeparateMicrophoneOutputPath, p);
            }

            if (!string.IsNullOrEmpty(request.SeparateLoopbackOutputPath))
            {
                using var r = OpenReaderOrSilence(request.LoopbackWavPath, targetFormat);
                var p = BuildPipeline(r, targetFormat, 1.0,
                    request.LoopbackStartOffsetTicks, request.MicrophoneStartOffsetTicks,
                    request.ExpectedDurationTicks, request.TargetSampleRate, "loopback-osobno");
                WriteTo16BitWav(request.SeparateLoopbackOutputPath, p);
            }
        }

        _logger.LogInformation("Miks zakończony: {Path}", request.OutputWavPath);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void WriteTo16BitWav(string path, ISampleProvider source)
    {
        var waveProvider = new SampleToWaveProvider16(source);
        using var writer = new WaveFileWriter(path, waveProvider.WaveFormat);
        var buffer = new byte[waveProvider.WaveFormat.AverageBytesPerSecond];
        int read;
        while ((read = waveProvider.Read(buffer, 0, buffer.Length)) > 0)
            writer.Write(buffer, 0, read);
    }

    private ISampleProvider BuildPipeline(
        WaveStream reader,
        WaveFormat targetFormat,
        double volume,
        long thisStartTicks,
        long otherStartTicks,
        long expectedDurationTicks,
        int targetSampleRate,
        string trackLabel)
    {
        ISampleProvider sample = reader.ToSampleProvider();
        var nominalSampleRate = sample.WaveFormat.SampleRate;
        var sourceFrames = (long)Math.Round(reader.TotalTime.TotalSeconds * nominalSampleRate);

        // Kanały → stereo
        if (sample.WaveFormat.Channels == 1)
            sample = new MonoToStereoSampleProvider(sample);
        else if (sample.WaveFormat.Channels > 2)
            sample = new StereoDownmixSampleProvider(sample);

        // Korekcja dryfu zegara urządzenia względem monotonicznego czasu sesji.
        // Jest rozłożona równomiernie na cały materiał przez ułamkowy resampling.
        var effectiveInputRate = (double)nominalSampleRate;
        var shouldCorrectDrift = false;
        var referenceTicks = Math.Min(thisStartTicks, otherStartTicks);
        var leadingDelayTicks = Math.Max(0, thisStartTicks - referenceTicks);
        var targetContentTicks = Math.Max(0, expectedDurationTicks - leadingDelayTicks);

        if (targetContentTicks > 0 && sourceFrames > 0)
        {
            var targetFrames = (long)Math.Round(
                TimeSpan.FromTicks(targetContentTicks).TotalSeconds * nominalSampleRate);
            if (targetFrames > 0)
            {
                var correction = DriftCorrectionCalculator.Calculate(
                    sourceFrames,
                    targetFrames,
                    nominalSampleRate,
                    maximumCorrectionPpm: 1_000,
                    minimumDrift: TimeSpan.FromMilliseconds(50));

                effectiveInputRate = correction.EffectiveInputRate;
                shouldCorrectDrift = correction.ShouldCorrect;

                _logger.LogInformation(
                    "Dryf ścieżki {Track}: source={SourceFrames}, target={TargetFrames}, " +
                    "measured={MeasuredPpm:F1}ppm, applied={AppliedPpm:F1}ppm, limited={Limited}",
                    trackLabel,
                    correction.SourceFrames,
                    correction.TargetFrames,
                    correction.MeasuredDriftPpm,
                    correction.AppliedCorrectionPpm,
                    correction.WasLimited);
            }
        }

        if (shouldCorrectDrift || nominalSampleRate != targetFormat.SampleRate)
        {
            sample = new PrecisionResamplingSampleProvider(
                sample,
                effectiveInputRate,
                targetFormat.SampleRate);
        }

        // Głośność
        if (Math.Abs(volume - 1.0) > 0.0001)
        {
            var vol = new VolumeSampleProvider(sample) { Volume = (float)volume };
            sample = vol;
        }

        // Cisza na początku jeśli to źródło wystartowało później
        // reference = wcześniejszy start
        if (thisStartTicks == 0 && otherStartTicks == 0)
            return sample;

        var silenceSamples = AudioMath.CalculateLeadingSilenceSamples(
            thisStartTicks, referenceTicks, targetSampleRate, 2);

        if (silenceSamples > 0)
            sample = new OffsetSampleProvider(sample) { DelayBySamples = silenceSamples };

        return sample;
    }

    private static WaveStream OpenReaderOrSilence(string path, WaveFormat targetFormat)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 44)
        {
            try
            {
                return new AudioFileReader(path);
            }
            catch
            {
                // fallback
            }
        }

        // 1 sekunda ciszy jako minimalne źródło
        return new SilenceWaveStream(targetFormat, TimeSpan.FromSeconds(1));
    }
}

/// <summary>
/// Prosty downmix do stereo (pierwsze dwa kanały + reszta uśredniona do L/R).
/// </summary>
internal sealed class StereoDownmixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _sourceBuffer;
    private readonly int _sourceChannels;

    public StereoDownmixSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        _sourceBuffer = new float[_sourceChannels * 1024];
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var framesNeeded = count / 2;
        var sourceSamplesNeeded = framesNeeded * _sourceChannels;
        var sourceRead = 0;
        var outIndex = offset;

        while (sourceRead < sourceSamplesNeeded)
        {
            var toRead = Math.Min(_sourceBuffer.Length, sourceSamplesNeeded - sourceRead);
            var n = _source.Read(_sourceBuffer, 0, toRead);
            if (n <= 0)
                return outIndex - offset;

            var frames = n / _sourceChannels;
            for (var f = 0; f < frames; f++)
            {
                var baseIdx = f * _sourceChannels;
                buffer[outIndex++] = _sourceBuffer[baseIdx];
                buffer[outIndex++] = _sourceChannels > 1 ? _sourceBuffer[baseIdx + 1] : _sourceBuffer[baseIdx];
            }
            sourceRead += n;
            if (n < toRead)
                break;
        }

        return outIndex - offset;
    }
}

/// <summary>
/// Miękki limiter na ISampleProvider.
/// </summary>
internal sealed class SoftLimiterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    public SoftLimiterSampleProvider(ISampleProvider source) => _source = source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        for (var i = 0; i < read; i++)
            buffer[offset + i] = AudioMath.SoftLimit(buffer[offset + i]);
        return read;
    }
}

/// <summary>
/// Strumień ciszy o zadanej długości.
/// </summary>
internal sealed class SilenceWaveStream : WaveStream
{
    private readonly WaveFormat _format;
    private readonly long _length;
    private long _position;

    public SilenceWaveStream(WaveFormat format, TimeSpan duration)
    {
        _format = format;
        _length = (long)(duration.TotalSeconds * format.AverageBytesPerSecond);
        _length -= _length % format.BlockAlign;
    }

    public override WaveFormat WaveFormat => _format;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = (int)Math.Min(count, _length - _position);
        remaining -= remaining % _format.BlockAlign;
        if (remaining <= 0) return 0;
        Array.Clear(buffer, offset, remaining);
        _position += remaining;
        return remaining;
    }
}
