using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Capture;

/// <summary>
/// Krótki test poziomu sygnału mikrofonu lub loopback (bez zapisu na dysk).
/// </summary>
public sealed class LevelMeterService : IDisposable
{
    private readonly ILogger<LevelMeterService> _logger;
    private WasapiCapture? _capture;
    private MMDevice? _device;
    private bool _running;

    public LevelMeterService(ILogger<LevelMeterService> logger)
    {
        _logger = logger;
    }

    public event EventHandler<AudioLevelEventArgs>? LevelChanged;

    public void StartMicrophoneTest(string deviceId)
    {
        Stop();
        var enumerator = new MMDeviceEnumerator();
        try
        {
            _device = enumerator.GetDevice(deviceId);
            _capture = new WasapiCapture(_device);
            _capture.DataAvailable += OnData;
            _capture.StartRecording();
            _running = true;
            _logger.LogInformation("Test mikrofonu: {Name}", _device.FriendlyName);
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    public void StartLoopbackTest(string deviceId)
    {
        Stop();
        var enumerator = new MMDeviceEnumerator();
        try
        {
            _device = enumerator.GetDevice(deviceId);
            _capture = new WasapiLoopbackCapture(_device);
            _capture.DataAvailable += OnData;
            _capture.StartRecording();
            _running = true;
            _logger.LogInformation("Test loopback: {Name}", _device.FriendlyName);
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    public void Stop()
    {
        if (!_running && _capture is null)
            return;

        try
        {
            if (_capture is not null)
            {
                _capture.DataAvailable -= OnData;
                _capture.StopRecording();
                _capture.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Stop level meter");
        }

        _capture = null;
        try { _device?.Dispose(); } catch { /* ignore */ }
        _device = null;
        _running = false;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _capture is null)
        {
            LevelChanged?.Invoke(this, new AudioLevelEventArgs(0, 0));
            return;
        }

        var format = _capture.WaveFormat;
        float peak = 0, sumSq = 0;
        var count = 0;

        if (format.BitsPerSample == 32 || format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            for (var i = 0; i + 4 <= e.BytesRecorded; i += 4)
            {
                var s = BitConverter.ToSingle(e.Buffer, i);
                var a = Math.Abs(s);
                if (a > peak) peak = a;
                sumSq += s * s;
                count++;
            }
        }
        else if (format.BitsPerSample == 16)
        {
            for (var i = 0; i + 2 <= e.BytesRecorded; i += 2)
            {
                var s = BitConverter.ToInt16(e.Buffer, i) / 32768f;
                var a = Math.Abs(s);
                if (a > peak) peak = a;
                sumSq += s * s;
                count++;
            }
        }

        var rms = count > 0 ? (float)Math.Sqrt(sumSq / count) : 0f;
        LevelChanged?.Invoke(this, new AudioLevelEventArgs(peak, rms));
    }

    public void Dispose() => Stop();
}
