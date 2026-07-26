using System.Diagnostics;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Capture;

/// <summary>
/// Przechwytywanie mikrofonu w trybie współdzielonym WASAPI do pliku WAV.
/// </summary>
public sealed class WasapiMicrophoneCapture : IMicrophoneCaptureService
{
    private readonly ILogger<WasapiMicrophoneCapture> _logger;
    private readonly object _sync = new();
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
    private MMDevice? _device;
    private bool _isCapturing;
    private long _samplesWritten;
    private long _startOffsetTicks;
    private WaveFormat? _format;
    private readonly Stopwatch _clock = new();
    private string? _outputWavPath;

    public WasapiMicrophoneCapture(ILogger<WasapiMicrophoneCapture> logger)
    {
        _logger = logger;
    }

    public event EventHandler<AudioLevelEventArgs>? LevelChanged;
    public event EventHandler<Exception>? CaptureError;

    public bool IsCapturing => _isCapturing;
    public WaveFormatInfo? CaptureFormat => _format is null
        ? null
        : new WaveFormatInfo
        {
            SampleRate = _format.SampleRate,
            Channels = _format.Channels,
            BitsPerSample = _format.BitsPerSample,
            Encoding = _format.Encoding.ToString()
        };
    public long StartOffsetTicks => _startOffsetTicks;
    public long SamplesWritten => Interlocked.Read(ref _samplesWritten);

    public Task StartAsync(string deviceId, string outputWavPath, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isCapturing)
                throw new InvalidOperationException("Przechwytywanie mikrofonu już trwa.");

            var enumerator = new MMDeviceEnumerator();
            try
            {
                _device = enumerator.GetDevice(deviceId);
                if (_device.State != DeviceState.Active)
                    throw new InvalidOperationException("Wybrany mikrofon nie jest aktywny.");

                _capture = new WasapiCapture(_device);
                _format = _capture.WaveFormat;
                Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);
                _writer = new WaveFileWriter(outputWavPath, _format);
                _outputWavPath = outputWavPath;

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _clock.Restart();
                _startOffsetTicks = Stopwatch.GetTimestamp();
                // Konwersja do TimeSpan ticks (100ns)
                _startOffsetTicks = (long)(_startOffsetTicks * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency));

                _samplesWritten = 0;
                _capture.StartRecording();
                _isCapturing = true;

                _logger.LogInformation(
                    "Mikrofon start: {Name}, format={Rate}Hz/{Ch}ch/{Bits}bit/{Encoding}, blockAlign={BlockAlign}",
                    _device.FriendlyName,
                    _format.SampleRate,
                    _format.Channels,
                    _format.BitsPerSample,
                    _format.Encoding,
                    _format.BlockAlign);
            }
            catch
            {
                CleanupUnsafe();
                enumerator.Dispose();
                throw;
            }

            enumerator.Dispose();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_isCapturing || _capture is null)
                return Task.CompletedTask;

            try
            {
                _capture.StopRecording();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "StopRecording mikrofon");
            }

            // Writer zamykany w RecordingStopped lub tutaj
            FinalizeWriter();
            _isCapturing = false;
            var frames = _format is null ? 0 : _samplesWritten / _format.Channels;
            _logger.LogInformation(
                "Mikrofon stop: frames={Frames}, bytes={Bytes}",
                frames,
                GetFileSize(_outputWavPath));
        }

        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            WaveFileWriter? writer;
            lock (_sync)
            {
                writer = _writer;
                if (writer is null || e.BytesRecorded <= 0)
                    return;

                writer.Write(e.Buffer, 0, e.BytesRecorded);
                var bytesPerSample = Math.Max(1, (_format?.BitsPerSample ?? 16) / 8);
                Interlocked.Add(ref _samplesWritten, e.BytesRecorded / bytesPerSample);
            }

            RaiseLevel(e.Buffer, e.BytesRecorded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataAvailable mikrofon");
            CaptureError?.Invoke(this, ex);
        }
    }

    private void RaiseLevel(byte[] buffer, int bytesRecorded)
    {
        if (_format is null || bytesRecorded <= 0)
            return;

        float peak = 0, sumSq = 0;
        var count = 0;

        if (_format.Encoding == WaveFormatEncoding.IeeeFloat || _format.BitsPerSample == 32)
        {
            for (var i = 0; i + 4 <= bytesRecorded; i += 4)
            {
                var sample = BitConverter.ToSingle(buffer, i);
                var a = Math.Abs(sample);
                if (a > peak) peak = a;
                sumSq += sample * sample;
                count++;
            }
        }
        else if (_format.BitsPerSample == 16)
        {
            for (var i = 0; i + 2 <= bytesRecorded; i += 2)
            {
                var sample = BitConverter.ToInt16(buffer, i) / 32768f;
                var a = Math.Abs(sample);
                if (a > peak) peak = a;
                sumSq += sample * sample;
                count++;
            }
        }

        var rms = count > 0 ? (float)Math.Sqrt(sumSq / count) : 0f;
        LevelChanged?.Invoke(this, new AudioLevelEventArgs(peak, rms));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            _logger.LogError(e.Exception, "RecordingStopped mikrofon z błędem");
            CaptureError?.Invoke(this, e.Exception);
        }

        lock (_sync)
        {
            FinalizeWriter();
            _isCapturing = false;
        }
    }

    private void FinalizeWriter()
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FinalizeWriter mikrofon");
        }
        finally
        {
            _writer = null;
        }
    }

    private void CleanupUnsafe()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            try { _capture.Dispose(); } catch { /* ignore */ }
            _capture = null;
        }

        try { _writer?.Dispose(); } catch { /* ignore */ }
        _writer = null;
        try { _device?.Dispose(); } catch { /* ignore */ }
        _device = null;
        _outputWavPath = null;
        _isCapturing = false;
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            CleanupUnsafe();
        }
        return ValueTask.CompletedTask;
    }

    private static long GetFileSize(string? path)
    {
        try { return path is not null && File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }
}
