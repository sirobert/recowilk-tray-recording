using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.App.ViewModels;

public partial class RecordingsViewModel : ObservableObject, IDisposable
{
    private readonly IRecordingCatalog _catalog;
    private readonly IRecowilkUploadQueue _uploads;
    private readonly ISettingsService _settings;
    private readonly ILogger<RecordingsViewModel> _logger;
    private IReadOnlyList<RecordingListItemViewModel> _all = [];
    private int _refreshing;

    public ObservableCollection<RecordingListItemViewModel> Recordings { get; } = [];
    public IReadOnlyList<string> Filters { get; } = ["Wszystkie", "Wymagają uwagi", "Oczekujące", "Wysłane", "Tylko lokalne"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryExportCommand))]
    private RecordingListItemViewModel? _selectedRecording;

    [ObservableProperty] private string _selectedFilter = "Wszystkie";
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public RecordingsViewModel(IRecordingCatalog catalog, IRecowilkUploadQueue uploads,
        ISettingsService settings, ILogger<RecordingsViewModel> logger)
    {
        _catalog = catalog;
        _uploads = uploads;
        _settings = settings;
        _logger = logger;
        _catalog.Changed += OnCatalogChanged;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        IsBusy = true;
        try
        {
            var directory = _settings.Current.RecordingsDirectory;
            var entries = await Task.Run(() =>
            {
                _catalog.ReconcileRecordingsDirectory(directory);
                return _catalog.List();
            });
            _all = entries.Select(x => new RecordingListItemViewModel(x)).ToArray();
            ApplyFilter();
            StatusMessage = $"Nagrania: {_all.Count}. Ostatnie odświeżenie: {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Odświeżanie katalogu nagrań");
            StatusMessage = "Nie udało się odczytać katalogu: " + ex.Message;
        }
        finally { IsBusy = false; Interlocked.Exchange(ref _refreshing, 0); }
    }

    partial void OnSelectedFilterChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var selectedId = SelectedRecording?.RecordingId;
        IEnumerable<RecordingListItemViewModel> query = _all;
        query = SelectedFilter switch
        {
            "Wymagają uwagi" => query.Where(x => x.NeedsAttention),
            "Oczekujące" => query.Where(x => x.IsPending),
            "Wysłane" => query.Where(x => x.ExportStatus == RecordingExportStatus.Exported),
            "Tylko lokalne" => query.Where(x => x.ExportStatus == RecordingExportStatus.LocalOnly),
            _ => query
        };
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(x => x.Title.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)
                || x.AudioFileName.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
        Recordings.Clear();
        foreach (var item in query) Recordings.Add(item);
        SelectedRecording = Recordings.FirstOrDefault(x => x.RecordingId == selectedId) ?? Recordings.FirstOrDefault();
    }

    private bool CanRetryExport() => SelectedRecording?.CanRetry == true && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRetryExport))]
    private async Task RetryExportAsync()
    {
        if (SelectedRecording is null) return;
        IsBusy = true;
        RetryExportCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await Task.Run(() => _uploads.RetryExport(SelectedRecording.RecordingId));
            StatusMessage = result.Message;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ręczne ponowienie eksportu {RecordingId}", SelectedRecording.RecordingId);
            StatusMessage = "Nie udało się ponowić eksportu: " + ex.Message;
        }
        finally { IsBusy = false; RetryExportCommand.NotifyCanExecuteChanged(); }
    }

    [RelayCommand]
    private void OpenRecording()
    {
        if (SelectedRecording is null || !File.Exists(SelectedRecording.AudioPath)) return;
        Process.Start(new ProcessStartInfo { FileName = SelectedRecording.AudioPath, UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedRecording is null) return;
        var directory = Path.GetDirectoryName(SelectedRecording.AudioPath);
        if (directory is not null && Directory.Exists(directory))
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
    }

    [RelayCommand]
    private void CopyTraceId()
    {
        if (!string.IsNullOrWhiteSpace(SelectedRecording?.TraceId)) Clipboard.SetText(SelectedRecording.TraceId);
    }

    private void OnCatalogChanged(object? sender, RecordingCatalogChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        _ = dispatcher.InvokeAsync(() => RefreshCommand.Execute(null));
    }

    public void Dispose() => _catalog.Changed -= OnCatalogChanged;
}

public sealed class RecordingListItemViewModel
{
    public RecordingListItemViewModel(RecordingCatalogEntry entry) => Entry = entry;
    public RecordingCatalogEntry Entry { get; }
    public Guid RecordingId => Entry.RecordingId;
    public string AudioPath => Entry.AudioPath;
    public string AudioFileName => Path.GetFileName(Entry.AudioPath);
    public string Title => Entry.Title;
    public string? Description => Entry.Description;
    public DateTimeOffset StartedAt => Entry.StartedAt;
    public DateTimeOffset? StoppedAt => Entry.StoppedAt;
    public DateTimeOffset? ScheduledAt => Entry.ScheduledAt;
    public string DurationText => TimeSpan.FromMilliseconds(Entry.DurationMs).ToString(Entry.DurationMs >= 3_600_000 ? @"h\:mm\:ss" : @"mm\:ss");
    public string SizeText => Entry.AudioSizeBytes >= 1024 * 1024 ? $"{Entry.AudioSizeBytes / 1024d / 1024d:F1} MB" : $"{Entry.AudioSizeBytes / 1024d:F0} KB";
    public string Provider => Entry.Provider;
    public string? MeetingUrl => Entry.MeetingUrl;
    public IReadOnlyList<RecordingCatalogParticipant> Participants => Entry.Participants;
    public int ParticipantCount => Entry.Participants.Count;
    public RecordingExportStatus ExportStatus => Entry.ExportStatus;
    public string ExportStatusText => Entry.ExportStatus switch
    {
        RecordingExportStatus.LocalOnly => "Tylko lokalnie",
        RecordingExportStatus.Queued => "W kolejce",
        RecordingExportStatus.Connecting => "Łączenie z RecoWilk",
        RecordingExportStatus.CreatingMeeting => "Tworzenie spotkania",
        RecordingExportStatus.InitializingUpload => "Przygotowanie uploadu",
        RecordingExportStatus.Uploading => Entry.TotalChunks > 0 ? $"Wysyłanie {Entry.UploadedChunks}/{Entry.TotalChunks}" : "Wysyłanie",
        RecordingExportStatus.Completing => "Finalizacja",
        RecordingExportStatus.RetryScheduled => Entry.NextAttemptAt is { } retry ? $"Ponowienie {retry.LocalDateTime:HH:mm:ss}" : "Oczekuje na ponowienie",
        RecordingExportStatus.WaitingForCredentials => "Wymaga poprawienia klucza",
        RecordingExportStatus.Exported => "Wysłano do RecoWilk",
        RecordingExportStatus.PermanentFailure => "Błąd trwały",
        RecordingExportStatus.MissingFile => "Brak lokalnego pliku",
        _ => Entry.ExportStatus.ToString()
    };
    public string ErrorText => Entry.HttpStatusCode is { } status
        ? $"HTTP {status}; {Entry.ErrorCategory}" : Entry.ErrorCategory ?? string.Empty;
    public string? TraceId => Entry.TraceId;
    public int Attempts => Entry.Attempts;
    public Guid? MeetingId => Entry.MeetingId;
    public Guid? AudioAssetId => Entry.AudioAssetId;
    public Guid? ProcessingJobId => Entry.ProcessingJobId;
    public bool NeedsAttention => Entry.ExportStatus is RecordingExportStatus.RetryScheduled
        or RecordingExportStatus.WaitingForCredentials or RecordingExportStatus.PermanentFailure or RecordingExportStatus.MissingFile;
    public bool IsPending => Entry.ExportStatus is RecordingExportStatus.Queued or RecordingExportStatus.Connecting
        or RecordingExportStatus.CreatingMeeting or RecordingExportStatus.InitializingUpload
        or RecordingExportStatus.Uploading or RecordingExportStatus.Completing or RecordingExportStatus.RetryScheduled;
    public bool CanRetry => File.Exists(Entry.AudioPath) && Entry.ExportStatus is RecordingExportStatus.LocalOnly
        or RecordingExportStatus.RetryScheduled or RecordingExportStatus.WaitingForCredentials or RecordingExportStatus.PermanentFailure;
}
