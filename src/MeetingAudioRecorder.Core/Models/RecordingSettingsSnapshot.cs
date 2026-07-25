namespace MeetingAudioRecorder.Core.Models;

/// <summary>
/// Niezmienna konfiguracja konkretnej sesji, utrwalana po rozstrzygnięciu fallbacków urządzeń.
/// </summary>
public sealed record RecordingSettingsSnapshot
{
    public required string MicrophoneDeviceId { get; init; }
    public required string OutputDeviceId { get; init; }
    public required string RecordingsDirectory { get; init; }
    public required string FileNameFormat { get; init; }
    public int Mp3BitrateKbps { get; init; }
    public int TargetSampleRate { get; init; }
    public double MicrophoneVolume { get; init; }
    public double LoopbackVolume { get; init; }
    public bool KeepSeparateTracks { get; init; }
    public bool OpenFolderAfterRecording { get; init; }

    public static RecordingSettingsSnapshot From(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new RecordingSettingsSnapshot
        {
            MicrophoneDeviceId = settings.MicrophoneDeviceId,
            OutputDeviceId = settings.OutputDeviceId,
            RecordingsDirectory = settings.RecordingsDirectory,
            FileNameFormat = settings.FileNameFormat,
            Mp3BitrateKbps = settings.Mp3BitrateKbps,
            TargetSampleRate = settings.TargetSampleRate,
            MicrophoneVolume = settings.MicrophoneVolume,
            LoopbackVolume = settings.LoopbackVolume,
            KeepSeparateTracks = settings.KeepSeparateTracks,
            OpenFolderAfterRecording = settings.OpenFolderAfterRecording
        };
    }
}
