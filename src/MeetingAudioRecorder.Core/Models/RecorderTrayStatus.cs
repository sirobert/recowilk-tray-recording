namespace MeetingAudioRecorder.Core.Models;

public enum TranslationActivityState { Off, Starting, Running, Stopping, Error }

public sealed record TranslationActivity(TranslationActivityState State = TranslationActivityState.Off, string Sources = "", string? Detail = null)
{
    public string Text => State switch
    {
        TranslationActivityState.Starting => "uruchamianie…",
        TranslationActivityState.Running => "działa" + (Sources.Length > 0 ? " · " + Sources : ""),
        TranslationActivityState.Stopping => "kończenie…",
        TranslationActivityState.Error => "błąd: " + Detail,
        _ => "wyłączone"
    };
}

public sealed record UploadActivity(string Text, bool NeedsAttention = false)
{
    public static UploadActivity From(IEnumerable<RecordingCatalogEntry> entries, bool enabled, Guid? activeRecordingId = null)
    {
        var items = entries.ToArray();
        int attention = items.Count(e => e.ExportStatus is RecordingExportStatus.PermanentFailure or RecordingExportStatus.MissingFile or RecordingExportStatus.WaitingForCredentials);
        var inProgress = items.Where(e => e.ExportStatus is RecordingExportStatus.Connecting or RecordingExportStatus.CreatingMeeting
            or RecordingExportStatus.InitializingUpload or RecordingExportStatus.Uploading or RecordingExportStatus.Completing).ToArray();
        var active = inProgress.Where(e => e.RecordingId == activeRecordingId).ToArray();
        int pending = items.Count(e => e.ExportStatus is RecordingExportStatus.Queued or RecordingExportStatus.RetryScheduled) + inProgress.Length - active.Length;
        var parts = new List<string>();
        if (!enabled) parts.Add("kolejka wyłączona");
        if (active.Length > 0)
        {
            long total = active.Sum(e => (long)Math.Max(0, e.TotalChunks));
            long sent = active.Sum(e => (long)Math.Clamp(e.UploadedChunks, 0, Math.Max(0, e.TotalChunks)));
            parts.Add($"wysyłanie: {active.Length}" + (total > 0 ? $" · {100 * sent / total}%" : ""));
        }
        if (pending > 0) parts.Add($"oczekuje: {pending}");
        if (attention > 0) parts.Add($"wymaga uwagi: {attention}");
        return new(parts.Count == 0 ? "brak oczekujących" : string.Join(" · ", parts), attention > 0);
    }
}

public sealed record RecorderTrayStatus
{
    public AppRecordingState Recording { get; init; }
    public TimeSpan Duration { get; init; }
    public string? RecordingError { get; init; }
    public TranslationActivity Translation { get; init; } = new();
    public UploadActivity Upload { get; init; } = new("sprawdzanie kolejki…");
    public bool RecordingMarker => Recording == AppRecordingState.Recording;
    public bool TranslationMarker => Translation.State is TranslationActivityState.Starting or TranslationActivityState.Running or TranslationActivityState.Stopping;
    public bool NeedsAttention => Recording == AppRecordingState.Error || Translation.State == TranslationActivityState.Error || Upload.NeedsAttention;
    public string RecordingText => "Nagrywanie: " + (Recording switch
    {
        AppRecordingState.Starting => "uruchamianie…",
        AppRecordingState.Recording => "trwa · " + (Duration.TotalHours >= 1 ? $"{(int)Duration.TotalHours}:{Duration:mm\\:ss}" : Duration.ToString(@"mm\:ss")),
        AppRecordingState.Stopping => "kończenie…",
        AppRecordingState.Processing => "zapis pliku…",
        AppRecordingState.Error => "błąd: " + RecordingError,
        _ => "gotowe"
    });
    public string TranslationText => "Tłumaczenie: " + Translation.Text;
    public string UploadText => "Wysyłka: " + Upload.Text;
    public string Tooltip => $"Recorder\n{RecordingText}\n{TranslationText}\n{UploadText}";
}
