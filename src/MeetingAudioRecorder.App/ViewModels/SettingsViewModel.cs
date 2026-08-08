using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetingAudioRecorder.Audio.Capture;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace MeetingAudioRecorder.App.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IAudioDeviceService _deviceService;
    private readonly IStartupService _startupService;
    private readonly IHotkeyService _hotkeyService;
    private readonly INotificationService _notificationService;
    private readonly IGoogleAuthorizationService _googleAuthorizationService;
    private readonly IMeetingAutomationService _meetingAutomationService;
    private readonly IBrowserExtensionInstaller _browserExtensionInstaller;
    private readonly LevelMeterService _levelMeter;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private ObservableCollection<AudioDeviceInfo> _microphones = new();
    [ObservableProperty] private ObservableCollection<AudioDeviceInfo> _outputDevices = new();
    [ObservableProperty] private AudioDeviceInfo? _selectedMicrophone;
    [ObservableProperty] private AudioDeviceInfo? _selectedOutput;
    [ObservableProperty] private string _recordingsDirectory = string.Empty;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private int _mp3BitrateKbps = 192;
    [ObservableProperty] private int _targetSampleRate = 48000;
    [ObservableProperty] private double _microphoneVolume = 1.0;
    [ObservableProperty] private double _loopbackVolume = 0.85;
    [ObservableProperty] private bool _keepSeparateTracks;
    [ObservableProperty] private bool _openFolderAfterRecording;
    [ObservableProperty] private string _fileNameFormat = "Nagranie_yyyy-MM-dd_HH-mm-ss.mp3";
    [ObservableProperty] private string _hotkeyKey = "R";
    [ObservableProperty] private bool _hotkeyControl = true;
    [ObservableProperty] private bool _hotkeyAlt = true;
    [ObservableProperty] private bool _hotkeyShift;
    [ObservableProperty] private bool _hotkeyWindows;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private double _micLevel;
    [ObservableProperty] private double _loopLevel;
    [ObservableProperty] private bool _isMicTesting;
    [ObservableProperty] private bool _isLoopTesting;
    [ObservableProperty] private bool _googleMeetAutomationEnabled;
    [ObservableProperty] private bool _isGoogleConnected;
    [ObservableProperty] private bool _isGoogleBusy;
    [ObservableProperty] private string _googleConnectionStatus = "Niepołączono z Google.";
    [ObservableProperty] private string _googleAutomationStatus = "Automatyczne nagrywanie jest wyłączone.";
    [ObservableProperty] private string _browserExtensionStatus = "Rozszerzenie nie zostało jeszcze przygotowane.";

    public int[] AvailableBitrates { get; } = [128, 192, 256, 320];
    public int[] AvailableSampleRates { get; } = [44100, 48000];

    public string HotkeyPreview =>
        new HotkeySettings
        {
            Key = HotkeyKey,
            Control = HotkeyControl,
            Alt = HotkeyAlt,
            Shift = HotkeyShift,
            Windows = HotkeyWindows
        }.DisplayText;

    public SettingsViewModel(
        ISettingsService settingsService,
        IAudioDeviceService deviceService,
        IStartupService startupService,
        IHotkeyService hotkeyService,
        INotificationService notificationService,
        IGoogleAuthorizationService googleAuthorizationService,
        IMeetingAutomationService meetingAutomationService,
        IBrowserExtensionInstaller browserExtensionInstaller,
        LevelMeterService levelMeter,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _deviceService = deviceService;
        _startupService = startupService;
        _hotkeyService = hotkeyService;
        _notificationService = notificationService;
        _googleAuthorizationService = googleAuthorizationService;
        _meetingAutomationService = meetingAutomationService;
        _browserExtensionInstaller = browserExtensionInstaller;
        _levelMeter = levelMeter;
        _logger = logger;

        _levelMeter.LevelChanged += OnLevelChanged;
        _meetingAutomationService.StatusChanged += OnMeetingAutomationStatusChanged;
        LoadFromSettings();
        RefreshDevices();
        _ = RefreshGoogleConnectionSafeAsync();
    }

    public void LoadFromSettings()
    {
        var s = _settingsService.Current;
        RecordingsDirectory = s.RecordingsDirectory;
        StartWithWindows = s.StartWithWindows;
        Mp3BitrateKbps = s.Mp3BitrateKbps;
        TargetSampleRate = s.TargetSampleRate;
        MicrophoneVolume = s.MicrophoneVolume;
        LoopbackVolume = s.LoopbackVolume;
        KeepSeparateTracks = s.KeepSeparateTracks;
        OpenFolderAfterRecording = s.OpenFolderAfterRecording;
        FileNameFormat = s.FileNameFormat;
        HotkeyKey = s.Hotkey.Key;
        HotkeyControl = s.Hotkey.Control;
        HotkeyAlt = s.Hotkey.Alt;
        HotkeyShift = s.Hotkey.Shift;
        HotkeyWindows = s.Hotkey.Windows;
        GoogleMeetAutomationEnabled = s.GoogleMeetAutomationEnabled;
        GoogleAutomationStatus = _meetingAutomationService.Status.Message;
        OnPropertyChanged(nameof(HotkeyPreview));
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        Microphones = new ObservableCollection<AudioDeviceInfo>(_deviceService.GetCaptureDevices());
        OutputDevices = new ObservableCollection<AudioDeviceInfo>(_deviceService.GetRenderDevices());

        var s = _settingsService.Current;
        SelectedMicrophone = Microphones.FirstOrDefault(d => d.Id == s.MicrophoneDeviceId)
                             ?? _deviceService.ResolveDevice(s.MicrophoneDeviceId, AudioDeviceType.Capture).Device
                             ?? Microphones.FirstOrDefault();

        SelectedOutput = OutputDevices.FirstOrDefault(d => d.Id == s.OutputDeviceId)
                         ?? _deviceService.ResolveDevice(s.OutputDeviceId, AudioDeviceType.Render).Device
                         ?? OutputDevices.FirstOrDefault();
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Wybierz folder nagrań",
            InitialDirectory = Directory.Exists(RecordingsDirectory)
                ? RecordingsDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dlg.ShowDialog() == true)
            RecordingsDirectory = dlg.FolderName;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            StopTests();

            if (GoogleMeetAutomationEnabled && !IsGoogleConnected)
            {
                StatusMessage = "Najpierw połącz konto Google.";
                MessageBox.Show(StatusMessage, "Google Meet", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var settings = _settingsService.Current;
            settings.MicrophoneDeviceId = SelectedMicrophone?.Id ?? string.Empty;
            settings.OutputDeviceId = SelectedOutput?.Id ?? string.Empty;
            settings.RecordingsDirectory = RecordingsDirectory;
            settings.StartWithWindows = StartWithWindows;
            settings.Mp3BitrateKbps = Mp3BitrateKbps;
            settings.TargetSampleRate = TargetSampleRate;
            settings.MicrophoneVolume = MicrophoneVolume;
            settings.LoopbackVolume = LoopbackVolume;
            settings.KeepSeparateTracks = KeepSeparateTracks;
            settings.OpenFolderAfterRecording = OpenFolderAfterRecording;
            settings.FileNameFormat = FileNameFormat;
            settings.GoogleMeetAutomationEnabled = GoogleMeetAutomationEnabled;
            settings.Hotkey = new HotkeySettings
            {
                Key = HotkeyKey,
                Control = HotkeyControl,
                Alt = HotkeyAlt,
                Shift = HotkeyShift,
                Windows = HotkeyWindows
            };

            var validation = _settingsService.Validate(settings);
            if (!validation.IsValid)
            {
                StatusMessage = string.Join(" ", validation.Errors);
                MessageBox.Show(StatusMessage, "Błąd ustawień", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!string.IsNullOrWhiteSpace(settings.RecordingsDirectory))
                Directory.CreateDirectory(settings.RecordingsDirectory);

            var commit = SettingsTransactionService.TryCommit(
                _settingsService,
                _hotkeyService,
                settings);
            if (!commit.Success)
            {
                StatusMessage = commit.ErrorMessage ?? "Nie udało się zapisać ustawień.";
                MessageBox.Show(StatusMessage, "Nie zapisano ustawień", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _startupService.SetEnabled(StartWithWindows);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Autostart");
                MessageBox.Show("Nie udało się zaktualizować autostartu Windows.", "Uwaga",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            StatusMessage = "Ustawienia zapisane.";
            _notificationService.ShowInfo("Ustawienia", "Ustawienia zostały zapisane.");
            await _meetingAutomationService.CheckNowAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Zapis ustawień");
            MessageBox.Show("Nie udało się zapisać ustawień: " + ex.Message, "Błąd",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ConnectGoogleAsync()
    {
        if (IsGoogleBusy)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Wybierz konfigurację OAuth Google typu Desktop app",
            Filter = "Konfiguracja Google OAuth (*.json)|*.json|Wszystkie pliki (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads")
        };
        if (dialog.ShowDialog() != true)
            return;

        var wasConnected = IsGoogleConnected;
        IsGoogleBusy = true;
        GoogleConnectionStatus = "Łączenie z Google w przeglądarce…";
        try
        {
            var connection = await _googleAuthorizationService.ConnectAsync(dialog.FileName).ConfigureAwait(true);
            IsGoogleConnected = connection.IsConnected;
            GoogleConnectionStatus = connection.IsConnected
                ? $"Połączono: {connection.AccountEmail}"
                : "Niepołączono z Google.";
            StatusMessage = connection.IsConnected
                ? "Konto Google zostało połączone. Włącz automatykę i zapisz ustawienia."
                : "Nie udało się połączyć konta Google.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Łączenie konta Google");
            IsGoogleConnected = wasConnected;
            if (!wasConnected)
                GoogleMeetAutomationEnabled = false;
            GoogleConnectionStatus = wasConnected
                ? "Nie udało się zmienić konta. Poprzednie połączenie pozostaje aktywne."
                : "Nie udało się połączyć z Google.";
            MessageBox.Show(ex.Message, "Google Meet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsGoogleBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectGoogleAsync()
    {
        if (IsGoogleBusy || !IsGoogleConnected)
            return;

        var answer = MessageBox.Show(
            "Odłączyć konto Google i wyłączyć automatyczne nagrywanie spotkań?",
            "Google Meet",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        IsGoogleBusy = true;
        try
        {
            await _googleAuthorizationService.DisconnectAsync().ConfigureAwait(true);
            IsGoogleConnected = false;
            GoogleMeetAutomationEnabled = false;
            GoogleConnectionStatus = "Niepołączono z Google.";

            var settings = _settingsService.Current;
            settings.GoogleMeetAutomationEnabled = false;
            _settingsService.Save(settings);
            await _meetingAutomationService.CheckNowAsync().ConfigureAwait(true);
            StatusMessage = "Konto Google zostało odłączone.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Odłączanie konta Google");
            MessageBox.Show(ex.Message, "Google Meet", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsGoogleBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshGoogleConnectionAsync()
    {
        if (IsGoogleBusy)
            return;

        IsGoogleBusy = true;
        try
        {
            var connection = await _googleAuthorizationService.GetConnectionInfoAsync().ConfigureAwait(true);
            IsGoogleConnected = connection.IsConnected;
            GoogleConnectionStatus = connection.IsConnected
                ? $"Połączono: {connection.AccountEmail}"
                : "Niepołączono z Google.";
            if (!connection.IsConnected)
                GoogleMeetAutomationEnabled = false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Odświeżanie stanu konta Google");
            IsGoogleConnected = false;
            GoogleMeetAutomationEnabled = false;
            GoogleConnectionStatus = "Nie można odczytać połączenia Google.";
        }
        finally
        {
            IsGoogleBusy = false;
        }
    }

    private async Task RefreshGoogleConnectionSafeAsync()
    {
        try
        {
            await RefreshGoogleConnectionAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Odczyt stanu konta Google");
            IsGoogleConnected = false;
            GoogleMeetAutomationEnabled = false;
            GoogleConnectionStatus = "Nie można odczytać połączenia Google.";
        }
    }

    [RelayCommand]
    private Task PrepareChromeExtensionAsync()
        => PrepareBrowserExtensionAsync(SupportedBrowser.Chrome);

    [RelayCommand]
    private Task PrepareEdgeExtensionAsync()
        => PrepareBrowserExtensionAsync(SupportedBrowser.Edge);

    private async Task PrepareBrowserExtensionAsync(SupportedBrowser browser)
    {
        try
        {
            var result = await _browserExtensionInstaller.PrepareAsync(browser).ConfigureAwait(true);
            Clipboard.SetText(result.ExtensionDirectory);
            BrowserExtensionStatus = $"Pakiet gotowy. ID: {result.ExtensionId}. Ścieżka została skopiowana.";
            MessageBox.Show(
                "1. Włącz Tryb dewelopera na otwartej stronie rozszerzeń.\n" +
                "2. Kliknij „Załaduj rozpakowane”.\n" +
                "3. Wskaż otwarty folder MeetingOrgniazerGemini (ścieżka jest też w schowku).\n\n" +
                "Po instalacji pozostaw aplikację uruchomioną w zasobniku.",
                "Meeting Orgniazer Gemini",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Przygotowanie rozszerzenia {Browser}", browser);
            BrowserExtensionStatus = "Nie udało się przygotować rozszerzenia.";
            MessageBox.Show(ex.Message, "Meeting Orgniazer Gemini", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void TestMicrophone()
    {
        if (SelectedMicrophone is null)
        {
            MessageBox.Show("Wybierz mikrofon.", "Test", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (IsMicTesting)
            {
                _levelMeter.Stop();
                IsMicTesting = false;
                MicLevel = 0;
                return;
            }

            _levelMeter.Stop();
            IsLoopTesting = false;
            LoopLevel = 0;
            _levelMeter.StartMicrophoneTest(SelectedMicrophone.Id);
            IsMicTesting = true;
            StatusMessage = "Test mikrofonu — mów do mikrofonu. Kliknij ponownie, aby zatrzymać.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test mikrofonu");
            MessageBox.Show(
                "Nie można otworzyć mikrofonu. Sprawdź, czy Windows zezwala aplikacji na dostęp do mikrofonu (Ustawienia → Prywatność → Mikrofon).",
                "Błąd mikrofonu", MessageBoxButton.OK, MessageBoxImage.Error);
            IsMicTesting = false;
        }
    }

    [RelayCommand]
    private void TestLoopback()
    {
        if (SelectedOutput is null)
        {
            MessageBox.Show("Wybierz urządzenie wyjściowe.", "Test", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (IsLoopTesting)
            {
                _levelMeter.Stop();
                IsLoopTesting = false;
                LoopLevel = 0;
                return;
            }

            _levelMeter.Stop();
            IsMicTesting = false;
            MicLevel = 0;
            _levelMeter.StartLoopbackTest(SelectedOutput.Id);
            IsLoopTesting = true;
            StatusMessage = "Test przechwytywania — odtwórz dźwięk na wybranym urządzeniu. Kliknij ponownie, aby zatrzymać.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test loopback");
            MessageBox.Show(
                "Nie można przechwycić dźwięku z wybranego urządzenia. Sprawdź, czy jest aktywne i nie jest używane w trybie wyłącznym.",
                "Błąd loopback", MessageBoxButton.OK, MessageBoxImage.Error);
            IsLoopTesting = false;
        }
    }

    private void OnLevelChanged(object? sender, AudioLevelEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        void Apply()
        {
            if (IsMicTesting)
                MicLevel = Math.Min(1.0, e.Peak * 1.5);
            if (IsLoopTesting)
                LoopLevel = Math.Min(1.0, e.Peak * 1.5);
        }

        if (dispatcher.CheckAccess())
            Apply();
        else
            _ = dispatcher.InvokeAsync(Apply);
    }

    private void OnMeetingAutomationStatusChanged(object? sender, MeetingAutomationStatus status)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        void Apply() => GoogleAutomationStatus = status.Message;

        if (dispatcher.CheckAccess())
            Apply();
        else
            _ = dispatcher.InvokeAsync(Apply);
    }

    public void StopTests()
    {
        _levelMeter.Stop();
        IsMicTesting = false;
        IsLoopTesting = false;
        MicLevel = 0;
        LoopLevel = 0;
    }

    partial void OnHotkeyKeyChanged(string value) => OnPropertyChanged(nameof(HotkeyPreview));
    partial void OnHotkeyControlChanged(bool value) => OnPropertyChanged(nameof(HotkeyPreview));
    partial void OnHotkeyAltChanged(bool value) => OnPropertyChanged(nameof(HotkeyPreview));
    partial void OnHotkeyShiftChanged(bool value) => OnPropertyChanged(nameof(HotkeyPreview));
    partial void OnHotkeyWindowsChanged(bool value) => OnPropertyChanged(nameof(HotkeyPreview));

    public void Dispose()
    {
        _levelMeter.LevelChanged -= OnLevelChanged;
        _meetingAutomationService.StatusChanged -= OnMeetingAutomationStatusChanged;
        StopTests();
        _levelMeter.Dispose();
    }
}
