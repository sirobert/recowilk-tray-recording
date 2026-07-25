using System.Diagnostics;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Capture;

/// <summary>
/// WASAPI Loopback — przechwytuje cały dźwięk wysyłany do wybranego urządzenia Render.
/// Uzupełnia ciszę, gdy callback nie dostarcza danych (brak odtwarzania).
/// </summary>
public sealed class WasapiLoopbackCaptureService : ILoopbackCaptureService
{
    private readonly ILogger<WasapiLoopbackCaptureService> _logger;
    private readonly object _sync = new();
    private WasapiLoopbackCapture? _capture;
    private WaveFileWriter? _writer;
    private MMDevice? _device;
    private bool _isCapturing;
    private long _samplesWritten;
    private long _startOffsetTicks;
    private WaveFormat? _format;
    private Timer? _silenceTimer;
    private long _lastDataTimestamp;
    private int _bytesPerFrame;
    private readonly Stopwatch _wallClock = new();

    public WasapiLoopbackCaptureService(ILogger<WasapiLoopbackCaptureService> logger)
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
                throw new InvalidOperationException("Przechwytywanie loopback już trwa.");

            var enumerator = new MMDeviceEnumerator();
            try
            {
                _device = enumerator.GetDevice(deviceId);
                if (_device.State != DeviceState.Active)
                    throw new InvalidOperationException("Wybrane urządzenie wyjściowe nie jest aktywne.");

                // WasapiLoopbackCapture przypisany do konkretnego MMDevice
                _capture = new WasapiLoopbackCapture(_device);
                _format = _capture.WaveFormat;
                _bytesPerFrame = _format.BlockAlign;

                Directory.CreateDirectory(Path.GetDirectoryName(outputWavPath)!);
                _writer = new WaveFileWriter(outputWavPath, _format);

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _startOffsetTicks = Stopwatch.GetTimestamp();
                _startOffsetTicks = (long)(_startOffsetTicks * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency));
                _samplesWritten = 0;
                _lastDataTimestamp = Stopwatch.GetTimestamp();
                _wallClock.Restart();

                _capture.StartRecording();
                _isCapturing = true;

                // Co 100 ms sprawdzaj, czy trzeba dopisać ciszę
                _silenceTimer = new Timer(FillSilenceIfNeeded, null, 100, 100);

                _logger.LogInformation(
                    "Loopback start: {Name}, format={Rate}Hz/{Ch}ch/{Bits}bit",
                    _device.FriendlyName, _format.SampleRate, _format.Channels, _format.BitsPerSample);
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

            _silenceTimer?.Dispose();
            _silenceTimer = null;

            try
            {
                _capture.StopRecording();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "StopRecording loopback");
            }

            // Dopisz brakującą ciszę do końca
            FillSilenceToNowUnsafe();
            FinalizeWriter();
            _isCapturing = false;
            _logger.LogInformation("Loopback stop, próbek={Samples}", _samplesWritten);
        }

        return Task.CompletedTask;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            lock (_sync)
            {
                if (_writer is null || !_isCapturing)
                    return;

                // Najpierw uzupełnij ciszę od ostatniego pakietu
                FillSilenceToNowUnsafe();

                if (e.BytesRecorded > 0)
                {
                    _writer.Write(e.Buffer, 0, e.BytesRecorded);
                    var bps = Math.Max(1, (_format?.BitsPerSample ?? 32) / 8);
                    Interlocked.Add(ref _samplesWritten, e.BytesRecorded / bps);
                    _lastDataTimestamp = Stopwatch.GetTimestamp();
                }
            }

            if (e.BytesRecorded > 0)
                RaiseLevel(e.Buffer, e.BytesRecorded);
            else
                LevelChanged?.Invoke(this, new AudioLevelEventArgs(0, 0));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataAvailable loopback");
            CaptureError?.Invoke(this, ex);
        }
    }

    private void FillSilenceIfNeeded(object? state)
    {
        try
        {
            lock (_sync)
            {
                if (!_isCapturing || _writer is null || _format is null)
                    return;
                FillSilenceToNowUnsafe();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FillSilence");
        }
    }

    /// <summary>
    /// Uzupełnia ścieżkę zerowymi próbkami tak, aby długość pliku odpowiadała czasowi ściennemu.
    /// </summary>
    private void FillSilenceToNowUnsafe()
    {
        if (_writer is null || _format is null || _bytesPerFrame <= 0)
            return;

        var expectedBytes = (long)(_wallClock.Elapsed.TotalSeconds * _format.AverageBytesPerSecond);
        var missing = expectedBytes - _writer.Length;
        if (missing < _bytesPerFrame)
            return;

        // Zaokrąglij w dół do pełnych ramek
        missing -= missing % _bytesPerFrame;
        if (missing <= 0)
            return;

        // Pisz ciszę w kawałkach
        var chunk = new byte[Math.Min(missing, _format.AverageBytesPerSecond / 2)]; // max ~0.5 s
        while (missing > 0)
        {
            var toWrite = (int)Math.Min(chunk.Length, missing);
            toWrite -= toWrite % _bytesPerFrame;
            if (toWrite <= 0) break;
            _writer.Write(chunk, 0, toWrite);
            var bps = Math.Max(1, _format.BitsPerSample / 8);
            Interlocked.Add(ref _samplesWritten, toWrite / bps);
            missing -= toWrite;
        }

        _lastDataTimestamp = Stopwatch.GetTimestamp();
    }

    private void RaiseLevel(byte[] buffer, int bytesRecorded)
    {
        if (_format is null || bytesRecorded <= 0)
            return;

        float peak = 0, sumSq = 0;
        var count = 0;

        if (_format.BitsPerSample == 32 || _format.Encoding == WaveFormatEncoding.IeeeFloat)
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
            _logger.LogError(e.Exception, "RecordingStopped loopback z błędem");
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
            _logger.LogWarning(ex, "FinalizeWriter loopback");
        }
        finally
        {
            _writer = null;
        }
    }

    private void CleanupUnsafe()
    {
        _silenceTimer?.Dispose();
        _silenceTimer = null;

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
}
