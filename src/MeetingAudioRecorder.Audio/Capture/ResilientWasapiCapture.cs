using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Capture;

/// <summary>
/// Minimalny capture WASAPI dla trybu współdzielonego. Pętla jest oparta na
/// NAudio 2.2.1 (MIT), ale zabezpiecza również AudioClient.Stop, którego wyjątek
/// w oryginalnej implementacji może opuścić wątek roboczy i zakończyć proces.
/// </summary>
internal sealed class ResilientWasapiCapture : IWaveIn
{
    private const long ReferenceTimesPerSecond = 10_000_000;
    private const long ReferenceTimesPerMillisecond = 10_000;
    private const int BufferMilliseconds = 100;

    private readonly AudioClient _audioClient;
    private readonly SynchronizationContext? _synchronizationContext;
    private readonly bool _loopback;
    private volatile CaptureState _captureState;
    private Thread? _captureThread;
    private byte[]? _recordBuffer;
    private WaveFormat _waveFormat;
    private int _bytesPerFrame;
    private bool _initialized;
    private bool _disposed;

    public ResilientWasapiCapture(MMDevice device, bool loopback)
    {
        ArgumentNullException.ThrowIfNull(device);
        _synchronizationContext = SynchronizationContext.Current;
        _audioClient = device.AudioClient;
        _waveFormat = _audioClient.MixFormat;
        _loopback = loopback;
    }

    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public WaveFormat WaveFormat
    {
        get => _waveFormat.AsStandardWaveFormat();
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized)
                throw new InvalidOperationException("Nie można zmienić formatu po uruchomieniu capture.");
            _waveFormat = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    public void StartRecording()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_captureState != CaptureState.Stopped)
            throw new InvalidOperationException("Poprzednie przechwytywanie nadal trwa.");

        _captureState = CaptureState.Starting;
        Initialize();
        _captureThread = new Thread(CaptureThread)
        {
            IsBackground = true,
            Name = _loopback ? "Recorder WASAPI loopback" : "Recorder WASAPI microphone"
        };
        _captureThread.Start();
    }

    public void StopRecording()
    {
        if (_captureState != CaptureState.Stopped)
            _captureState = CaptureState.Stopping;
    }

    private void Initialize()
    {
        if (_initialized)
            return;

        var requestedDuration = ReferenceTimesPerMillisecond * BufferMilliseconds;
        var flags = AudioClientStreamFlags.AutoConvertPcm | AudioClientStreamFlags.SrcDefaultQuality;
        if (_loopback)
            flags |= AudioClientStreamFlags.Loopback;

        _audioClient.Initialize(
            AudioClientShareMode.Shared,
            flags,
            requestedDuration,
            0,
            _waveFormat,
            Guid.Empty);

        _bytesPerFrame = _waveFormat.BlockAlign;
        _recordBuffer = new byte[_audioClient.BufferSize * _bytesPerFrame];
        _initialized = true;
    }

    private void CaptureThread()
    {
        var error = CaptureThreadExceptionPolicy.Execute(
            () => CaptureLoop(_audioClient),
            _audioClient.Stop);

        _captureState = CaptureState.Stopped;
        _captureThread = null;

        try
        {
            RaiseRecordingStopped(error);
        }
        catch
        {
            // Granica wątku capture nie może zakończyć całego procesu. Handlery
            // produkcyjne logują błąd przed powrotem z wywołania.
        }
    }

    private void CaptureLoop(AudioClient client)
    {
        var bufferFrameCount = client.BufferSize;
        var actualDuration = (long)(ReferenceTimesPerSecond * (double)bufferFrameCount / _waveFormat.SampleRate);
        var sleepMilliseconds = Math.Max(1, (int)(actualDuration / ReferenceTimesPerMillisecond / 2));
        var captureClient = client.AudioCaptureClient;

        client.Start();
        if (_captureState == CaptureState.Starting)
            _captureState = CaptureState.Capturing;

        while (_captureState == CaptureState.Capturing)
        {
            Thread.Sleep(sleepMilliseconds);
            if (_captureState != CaptureState.Capturing)
                break;

            ReadNextPacket(captureClient);
        }
    }

    private void ReadNextPacket(AudioCaptureClient captureClient)
    {
        var recordBuffer = _recordBuffer ?? throw new InvalidOperationException("Bufor capture nie został zainicjalizowany.");
        var packetSize = captureClient.GetNextPacketSize();
        var recordBufferOffset = 0;

        while (packetSize != 0)
        {
            var buffer = captureClient.GetBuffer(out var framesAvailable, out var flags);
            var bytesAvailable = checked(framesAvailable * _bytesPerFrame);
            var spaceRemaining = Math.Max(0, recordBuffer.Length - recordBufferOffset);

            if (spaceRemaining < bytesAvailable && recordBufferOffset > 0)
            {
                DataAvailable?.Invoke(this, new WaveInEventArgs(recordBuffer, recordBufferOffset));
                recordBufferOffset = 0;
                spaceRemaining = recordBuffer.Length;
            }

            if (bytesAvailable > spaceRemaining)
                throw new InvalidOperationException("Pakiet WASAPI przekracza pojemność bufora capture.");

            if ((flags & AudioClientBufferFlags.Silent) == 0)
                Marshal.Copy(buffer, recordBuffer, recordBufferOffset, bytesAvailable);
            else
                Array.Clear(recordBuffer, recordBufferOffset, bytesAvailable);

            recordBufferOffset += bytesAvailable;
            captureClient.ReleaseBuffer(framesAvailable);
            packetSize = captureClient.GetNextPacketSize();
        }

        DataAvailable?.Invoke(this, new WaveInEventArgs(recordBuffer, recordBufferOffset));
    }

    private void RaiseRecordingStopped(Exception? error)
    {
        var handler = RecordingStopped;
        if (handler is null)
            return;

        var args = new StoppedEventArgs(error);
        if (_synchronizationContext is null)
            handler(this, args);
        else
            _synchronizationContext.Post(_ => handler(this, args), null);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopRecording();
        var thread = _captureThread;
        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join();

        _captureThread = null;
        _audioClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
