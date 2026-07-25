namespace MeetingAudioRecorder.Core.Interfaces;

public interface IAudioMixingService
{
    /// <summary>
    /// Miksuje dwa pliki WAV (mikrofon + loopback) do jednego pliku WAV stereo.
    /// </summary>
    Task MixToWavAsync(MixRequest request, CancellationToken cancellationToken = default);
}

public sealed class MixRequest
{
    public required string MicrophoneWavPath { get; init; }
    public required string LoopbackWavPath { get; init; }
    public required string OutputWavPath { get; init; }
    public long MicrophoneStartOffsetTicks { get; init; }
    public long LoopbackStartOffsetTicks { get; init; }
    public long ExpectedDurationTicks { get; init; }
    public int TargetSampleRate { get; init; } = 48000;
    public double MicrophoneVolume { get; init; } = 1.0;
    public double LoopbackVolume { get; init; } = 0.85;
    public bool KeepSeparateTracks { get; init; }
    public string? SeparateMicrophoneOutputPath { get; init; }
    public string? SeparateLoopbackOutputPath { get; init; }
}
