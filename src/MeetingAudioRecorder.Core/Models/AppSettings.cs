namespace MeetingAudioRecorder.Core.Models;

/// <summary>
/// Konfiguracja aplikacji zapisywana w settings.json.
/// </summary>
public sealed class AppSettings
{
    public string MicrophoneDeviceId { get; set; } = string.Empty;
    public string OutputDeviceId { get; set; } = string.Empty;
    public string RecordingsDirectory { get; set; } = string.Empty;
    public bool StartWithWindows { get; set; } = true;
    public HotkeySettings Hotkey { get; set; } = new();
    public int Mp3BitrateKbps { get; set; } = 192;
    public int TargetSampleRate { get; set; } = 48000;
    public double MicrophoneVolume { get; set; } = 1.0;
    public double LoopbackVolume { get; set; } = 0.85;
    public bool KeepSeparateTracks { get; set; }
    public bool OpenFolderAfterRecording { get; set; }
    public string FileNameFormat { get; set; } = "Nagranie_yyyy-MM-dd_HH-mm-ss.mp3";
    public bool ConsentAcknowledged { get; set; }

    public static AppSettings CreateDefault()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return new AppSettings
        {
            RecordingsDirectory = Path.Combine(documents, "Nagrania spotkań"),
            StartWithWindows = true,
            Hotkey = new HotkeySettings
            {
                Key = "R",
                Control = true,
                Alt = true,
                Shift = false,
                Windows = false
            },
            Mp3BitrateKbps = 192,
            TargetSampleRate = 48000,
            MicrophoneVolume = 1.0,
            LoopbackVolume = 0.85,
            KeepSeparateTracks = false,
            OpenFolderAfterRecording = false,
            FileNameFormat = "Nagranie_yyyy-MM-dd_HH-mm-ss.mp3",
            ConsentAcknowledged = false
        };
    }

    public AppSettings Clone() => new()
    {
        MicrophoneDeviceId = MicrophoneDeviceId,
        OutputDeviceId = OutputDeviceId,
        RecordingsDirectory = RecordingsDirectory,
        StartWithWindows = StartWithWindows,
        Hotkey = Hotkey.Clone(),
        Mp3BitrateKbps = Mp3BitrateKbps,
        TargetSampleRate = TargetSampleRate,
        MicrophoneVolume = MicrophoneVolume,
        LoopbackVolume = LoopbackVolume,
        KeepSeparateTracks = KeepSeparateTracks,
        OpenFolderAfterRecording = OpenFolderAfterRecording,
        FileNameFormat = FileNameFormat,
        ConsentAcknowledged = ConsentAcknowledged
    };
}
