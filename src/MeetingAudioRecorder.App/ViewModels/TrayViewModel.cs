using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.App.ViewModels;

public partial class TrayViewModel : ObservableObject
{
    private readonly IRecordingCoordinator _coordinator;
    private readonly ISettingsService _settingsService;
    private readonly INotificationService _notificationService;
    private readonly ILogger<TrayViewModel> _logger;
    private readonly Action _openSettings;
    private readonly Action _openRecordings;
    private string? _lastOutputPath;
    private bool _isExiting;

    [ObservableProperty] private string _statusText = "Gotowy";
    [ObservableProperty] private string _durationText = string.Empty;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _canStart = true;
    [ObservableProperty] private bool _canStop;

    public TrayViewModel(
        IRecordingCoordinator coordinator,
        ISettingsService settingsService,
        INotificationService notificationService,
        ILogger<TrayViewModel> logger,
        Action openSettings,
        Action openRecordings)
    {
        _coordinator = coordinator;
        _settingsService = settingsService;
        _notificationService = notificationService;
        _logger = logger;
        _openSettings = openSettings;
        _openRecordings = openRecordings;

        _coordinator.StateChanged += OnStateChanged;
        _coordinator.DurationUpdated += OnDuration;
    }

    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        try
        {
            await _coordinator.StartRecordingAsync().ConfigureAwait(true);
            _notificationService.ShowInfo("Nagrywanie", "Rozpoczęto nagrywanie spotkania.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start z tray");
            _notificationService.ShowError("Nie można nagrywać", UserMessage(ex));
            MessageBox.Show(UserMessage(ex), "Nagrywanie", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        try
        {
            var result = await _coordinator.StopRecordingAsync().ConfigureAwait(true);
            if (result.Success && result.OutputPath is not null)
            {
                _lastOutputPath = result.OutputPath;
                var msg = $"Zapisano: {Path.GetFileName(result.OutputPath)}\nCzas: {FormatDuration(result.Duration)}\n{result.OutputPath}";
                _notificationService.ShowSuccess("Nagranie gotowe", msg, result.OutputPath);

                if (_coordinator.CurrentSession?.SettingsSnapshot.OpenFolderAfterRecording
                    ?? _settingsService.Current.OpenFolderAfterRecording)
                    OpenPath(Path.GetDirectoryName(result.OutputPath)!);
            }
            else
            {
                _notificationService.ShowError("Błąd zapisu", result.ErrorMessage ?? "Nie udało się zapisać nagrania.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stop z tray");
            _notificationService.ShowError("Błąd", UserMessage(ex));
        }
    }

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        if (_coordinator.State == AppRecordingState.Recording)
            await StopRecordingAsync();
        else if (_coordinator.CanStart)
            await StartRecordingAsync();
        else if (_coordinator.State is AppRecordingState.Processing or AppRecordingState.Stopping or AppRecordingState.Starting)
        {
            _notificationService.ShowWarning("Proszę czekać",
                "Trwa zapisywanie lub przetwarzanie poprzedniego nagrania.");
        }
    }

    [RelayCommand]
    private void OpenSettings() => _openSettings();

    [RelayCommand]
    private void OpenRecordings() => _openRecordings();

    [RelayCommand]
    private void OpenRecordingsFolder()
    {
        var dir = _settingsService.Current.RecordingsDirectory;
        try
        {
            Directory.CreateDirectory(dir);
            OpenPath(dir);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Nie można otworzyć folderu: " + ex.Message, "Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void ShowLastRecording()
    {
        if (_lastOutputPath is not null && File.Exists(_lastOutputPath))
        {
            OpenPath(Path.GetDirectoryName(_lastOutputPath)!);
            try
            {
                Process.Start(new ProcessStartInfo { FileName = _lastOutputPath, UseShellExecute = true });
            }
            catch { /* folder już otwarty */ }
            return;
        }

        // Ostatni plik MP3 w folderze
        var dir = _settingsService.Current.RecordingsDirectory;
        if (Directory.Exists(dir))
        {
            var last = Directory.EnumerateFiles(dir, "*.mp3")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (last is not null)
            {
                OpenPath(dir);
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = last.FullName, UseShellExecute = true });
                }
                catch { /* ignore */ }
                return;
            }
        }

        MessageBox.Show("Brak zapisanego nagrania.", "Ostatnie nagranie", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task ExitAsync()
    {
        if (_isExiting)
            return;

        var choice = UserExitChoice.Cancel;
        if (_coordinator.State == AppRecordingState.Recording)
        {
            var r = MessageBox.Show(
                "Trwa nagrywanie.\n\n" +
                "Tak — zatrzymaj i zapisz MP3 przed wyjściem.\n" +
                "Nie — zakończ aplikację i zachowaj pliki tymczasowe do odzyskania.",
                "Wyjście",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            choice = r switch
            {
                MessageBoxResult.Yes => UserExitChoice.SaveRecording,
                MessageBoxResult.No => UserExitChoice.PreserveTemporaryFiles,
                _ => UserExitChoice.Cancel
            };
        }

        var action = ShutdownPolicy.Decide(_coordinator.State, choice);
        if (action == ShutdownAction.Cancel)
            return;

        if (action == ShutdownAction.WaitForOperation)
        {
            _notificationService.ShowWarning(
                "Proszę czekać",
                "Trwa uruchamianie, zatrzymywanie lub zapis nagrania. Zamknij aplikację po zakończeniu operacji.");
            return;
        }

        _isExiting = true;
        if (action == ShutdownAction.StopAndSave)
        {
            StatusText = "Zapisywanie przed wyjściem…";
            RecordingResult? result = null;
            try
            {
                result = await _coordinator.StopRecordingAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stop przy wyjściu");
            }

            if (result?.Success != true)
            {
                var error = result?.ErrorMessage ?? "Nie udało się zakończyć zapisu.";
                var exitAnyway = MessageBox.Show(
                    $"{error}\n\nPliki tymczasowe zostały zachowane. Zakończyć aplikację i odzyskać nagranie przy następnym uruchomieniu?",
                    "Nie udało się zapisać",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (exitAnyway != MessageBoxResult.Yes)
                {
                    _isExiting = false;
                    return;
                }
            }
        }

        await _coordinator.DisposeAsync();
        Application.Current.Shutdown();
    }

    private void OnStateChanged(object? sender, RecordingStateChangedEventArgs e)
    {
        RunOnUi(() =>
        {
            IsRecording = e.Current == AppRecordingState.Recording;
            CanStart = e.Current is AppRecordingState.Idle or AppRecordingState.Completed or AppRecordingState.Error;
            CanStop = e.Current == AppRecordingState.Recording;
            StatusText = e.Current switch
            {
                AppRecordingState.Idle => "Gotowy",
                AppRecordingState.Starting => "Uruchamianie…",
                AppRecordingState.Recording => e.Message ?? "Nagrywanie",
                AppRecordingState.Stopping => "Zatrzymywanie…",
                AppRecordingState.Processing => "Przetwarzanie…",
                AppRecordingState.Completed => "Zapisano",
                AppRecordingState.Error => "Błąd: " + (e.Message ?? ""),
                _ => e.Current.ToString()
            };
        });
    }

    private void OnDuration(object? sender, TimeSpan duration)
    {
        RunOnUi(() =>
        {
            DurationText = FormatDuration(duration);
        });
    }

    private static string FormatDuration(TimeSpan t)
        => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private static string UserMessage(Exception ex) => ex.Message;

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        if (dispatcher.CheckAccess())
            action();
        else
            _ = dispatcher.InvokeAsync(action);
    }
}
