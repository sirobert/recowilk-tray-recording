using System.Diagnostics;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Capture;

/// <summary>
/// WASAPI Loopback — przechwytuje cały dźwięk wysyłany do wybranego urządzenia Render.
/// Uzupełnia potwierdzone luki ciszą na podstawie monotonicznej osi czasu ramek.
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
    private int _bytesPerFrame;
    private LoopbackFrameTimeline? _timeline;
    private long _silenceFramesWritten;
    private string? _outputWavPath;
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
                _outputWavPath = outputWavPath;

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _startOffsetTicks = Stopwatch.GetTimestamp();
                _startOffsetTicks = (long)(_startOffsetTicks * (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency));
                _samplesWritten = 0;
                _silenceFramesWritten = 0;
                _timeline = new LoopbackFrameTimeline(
                    _format.SampleRate,
                    gapToleranceFrames: _format.SampleRate / 20); // 50 ms tolerancji opóźnienia callbacka
                _wallClock.Restart();

                _capture.StartRecording();
                _isCapturing = true;

                _logger.LogInformation(
                    "Loopback start: {Name}, format={Rate}Hz/{Ch}ch/{Bits}bit/{Encoding}, blockAlign={BlockAlign}",
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

            // Ostatnia cisza jest znana dopiero przy stopie. Wypełnij ją przed
            // StopRecording, ponieważ RecordingStopped może zamknąć writer.
            FillSilenceToNowUnsafe();

            try
            {
                _capture.StopRecording();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "StopRecording loopback");
            }

            FinalizeWriter();
            _isCapturing = false;
            var frames = _timeline?.PositionFrames
                         ?? (_format is null ? 0 : _samplesWritten / _format.Channels);
            _logger.LogInformation(
                "Loopback stop: frames={Frames}, silenceFrames={SilenceFrames}, bytes={Bytes}",
                frames,
                _silenceFramesWritten,
                GetFileSize(_outputWavPath));
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

                if (e.BytesRecorded > 0)
                {
                    var audioBytes = e.BytesRecorded - e.BytesRecorded % _bytesPerFrame;
                    var audioFrames = audioBytes / _bytesPerFrame;
                    var plan = _timeline!.PlanPacket(_wallClock.Elapsed.Ticks, audioFrames);

                    WriteSilenceFramesUnsafe(plan.SilenceFrames);
                    if (audioBytes > 0)
                    {
                        _writer.Write(e.Buffer, 0, audioBytes);
                        Interlocked.Add(ref _samplesWritten, (long)audioFrames * _format!.Channels);
                    }
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

    /// <summary>
    /// Domyka ścieżkę do czasu ściennego. Wywoływać tylko przy zatrzymaniu,
    /// gdy żaden przyszły pakiet nie może już reprezentować tego przedziału.
    /// </summary>
    private void FillSilenceToNowUnsafe()
    {
        if (_writer is null || _format is null || _timeline is null || _bytesPerFrame <= 0)
            return;

        var missingFrames = _timeline.PlanCompletion(_wallClock.Elapsed.Ticks);
        WriteSilenceFramesUnsafe(missingFrames);
    }

    private void WriteSilenceFramesUnsafe(long frames)
    {
        if (_writer is null || _format is null || frames <= 0)
            return;

        var maxChunkBytes = Math.Max(_bytesPerFrame, _format.AverageBytesPerSecond / 2);
        maxChunkBytes -= maxChunkBytes % _bytesPerFrame;
        var chunk = new byte[maxChunkBytes];
        while (frames > 0)
        {
            var framesInChunk = (int)Math.Min(frames, chunk.Length / _bytesPerFrame);
            var bytesToWrite = framesInChunk * _bytesPerFrame;
            _writer.Write(chunk, 0, bytesToWrite);
            Interlocked.Add(ref _samplesWritten, (long)framesInChunk * _format.Channels);
            _silenceFramesWritten += framesInChunk;
            frames -= framesInChunk;
        }
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
        _timeline = null;
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
