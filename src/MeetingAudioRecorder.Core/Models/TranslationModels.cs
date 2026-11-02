namespace MeetingAudioRecorder.Core.Models;

public sealed class TranslationSettings
{
    public bool OutputEnabled { get; set; } = true;
    public bool MicrophoneEnabled { get; set; }
    public bool Turbo { get; set; }
    public string ModelsDirectory { get; set; } = Path.Combine(AppPaths.LocalAppData, "Models");
    public HotkeySettings Hotkey { get; set; } = new() { Key = "T" };
    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 14;
    public string Foreground { get; set; } = "#F4F4F4";
    public string Background { get; set; } = "#20242A";
    public double BackgroundOpacity { get; set; } = .88;
    public bool ShowOriginal { get; set; }
    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 660;
    public double Height { get; set; } = 220;
    public TranslationSettings Clone() { var copy = (TranslationSettings)MemberwiseClone(); copy.Hotkey = (Hotkey ?? new() { Key = "T" }).Clone(); return copy; }
}

public sealed record TranslationCaption(string Source, int Id, string English, string Polish, bool IsDraft, double StartMs, double EndMs, string Status);
public sealed class TranslationSession
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Tłumaczenie live";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StoppedAt { get; set; }
    public string Sources { get; set; } = "output";
    public string MicrophoneDeviceId { get; set; } = "";
    public string OutputDeviceId { get; set; } = "";
    public string Profile { get; set; } = "small.en Q8 / Marian int8";
    public string State { get; set; } = "running";
    public string Display => $"{StartedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {Title} · {StateLabel}";
    public string StateLabel => State switch { "completed" => "Zakończona", "error" => "Przerwana — błąd", _ => "W toku / niedomknięta" };
}
public sealed record TranslationDocument(TranslationSession Session, IReadOnlyList<TranslationCaption> Captions, int UnreadableSegments);
public sealed record TranslationWorkerStart(string ModelsDirectory, bool Turbo, string Source, string DeviceId, string? WavPath = null);
public sealed record TranslationWorkerEvent(string Kind, string? Message = null, TranslationCaption? Caption = null);
