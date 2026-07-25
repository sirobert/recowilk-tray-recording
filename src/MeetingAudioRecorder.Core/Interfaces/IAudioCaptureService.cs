using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

/// <summary>
/// Abstrakcja przechwytywania jednego źródła audio do pliku tymczasowego.
/// Architektura umożliwia w przyszłości process loopback.
/// </summary>
public interface IAudioCaptureService : IAsyncDisposable
{
    event EventHandler<AudioLevelEventArgs>? LevelChanged;
    event EventHandler<Exception>? CaptureError;

    bool IsCapturing { get; }
    WaveFormatInfo? CaptureFormat { get; }
    long StartOffsetTicks { get; }
    long SamplesWritten { get; }

    Task StartAsync(string deviceId, string outputWavPath, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Minimalny opis formatu niezależny od NAudio (Core nie zależy od NAudio).
/// </summary>
public sealed class WaveFormatInfo
{
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public int BitsPerSample { get; init; }
    public string Encoding { get; init; } = "IeeeFloat";
}

public interface IMicrophoneCaptureService : IAudioCaptureService
{
}

public interface ILoopbackCaptureService : IAudioCaptureService
{
}
