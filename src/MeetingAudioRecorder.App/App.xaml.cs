using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using MeetingAudioRecorder.App.Services;
using MeetingAudioRecorder.App.ViewModels;
using MeetingAudioRecorder.App.Views;
using MeetingAudioRecorder.Audio.DependencyInjection;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using MeetingAudioRecorder.Infrastructure.DependencyInjection;
using MeetingAudioRecorder.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private ISingleInstanceService? _singleInstance;
    private TaskbarIcon? _tray;
    private TrayIconController? _trayIcons;
    private HotkeyService? _hotkeyService;
    private HostWindow? _hostWindow;
    private SettingsWindow? _settingsWindow;
    private ILogger<App>? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        _logger = _services.GetRequiredService<ILogger<App>>();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        _logger.LogInformation("=== Meeting Audio Recorder v{Version} start ===", version);

        _singleInstance = _services.GetRequiredService<ISingleInstanceService>();
        if (!_singleInstance.TryAcquire())
        {
            MessageBox.Show(
                "Aplikacja Meeting Audio Recorder jest już uruchomiona.\nSprawdź ikonę w zasobniku systemowym.",
                "Już uruchomiona",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppPaths.EnsureDirectories();

        var settingsService = _services.GetRequiredService<ISettingsService>();
        var settings = settingsService.Load();

        // Host window (niewidoczne) — wymagane do RegisterHotKey i message loop
        _hostWindow = new HostWindow();
        _hostWindow.Show();
        _hostWindow.Hide();

        _hotkeyService = (HotkeyService)_services.GetRequiredService<IHotkeyService>();
        _hotkeyService.Attach(_hostWindow.Handle);
        _hotkeyService.HotkeyPressed += async (_, _) =>
        {
            try
            {
                var trayVm = _services.GetRequiredService<TrayViewModel>();
                await trayVm.ToggleRecordingCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Hotkey toggle");
                _services?.GetService<INotificationService>()?.ShowError("Skrót", ex.Message);
            }
        };

        if (!_hotkeyService.Register(settings.Hotkey))
        {
            _logger.LogWarning("Skrót niedostępny: {Err}", _hotkeyService.LastError);
            MessageBox.Show(
                _hotkeyService.LastError ?? "Nie udało się zarejestrować skrótu klawiszowego. Zmień skrót w ustawieniach.",
                "Skrót klawiszowy",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // Tray
        _tray = (TaskbarIcon)FindResource("TrayIcon");
        var notificationService = (TrayNotificationService)_services.GetRequiredService<INotificationService>();
        notificationService.Attach(_tray);

        var trayVm = _services.GetRequiredService<TrayViewModel>();
        _tray.DataContext = trayVm;
        _tray.IconSource = TrayIconController.CreateIcon(AppRecordingState.Idle);
        _trayIcons = new TrayIconController(_tray);

        var coordinator = _services.GetRequiredService<IRecordingCoordinator>();
        coordinator.StateChanged += (_, args) =>
        {
            _trayIcons?.Update(args.Current, coordinator.CurrentDuration, args.Message);
            if (args.Previous == AppRecordingState.Recording
                && args.Current == AppRecordingState.Recording
                && !string.IsNullOrWhiteSpace(args.Message))
            {
                notificationService.ShowWarning("Mało miejsca na dysku", args.Message);
            }
        };
        coordinator.DurationUpdated += (_, duration) =>
        {
            if (coordinator.State == AppRecordingState.Recording)
                _trayIcons?.Update(AppRecordingState.Recording, duration);
        };

        // Device watch
        var devices = _services.GetRequiredService<IAudioDeviceService>();
        devices.StartWatching();
        devices.DeviceChanged += OnDeviceChanged;

        // Consent
        if (!settings.ConsentAcknowledged)
        {
            var consent = new ConsentWindow();
            if (consent.ShowDialog() == true)
            {
                settings.ConsentAcknowledged = true;
                settingsService.Save(settings);
            }
        }

        // Recovery
        var recovery = _services.GetRequiredService<IRecordingRecoveryService>();
        var recoverable = recovery.FindRecoverableRecordings();
        if (recoverable.Count > 0)
        {
            var win = new RecoveryWindow(recoverable, recovery, coordinator);
            win.ShowDialog();
        }

        // Device fallback warnings
        WarnIfDevicesMissing(settings, devices);

        // Sync autostart
        try
        {
            var startup = _services.GetRequiredService<IStartupService>();
            if (settings.StartWithWindows != startup.IsEnabled)
                startup.SetEnabled(settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Synchronizacja autostartu");
        }

        _logger.LogInformation("Aplikacja gotowa (tray). Skrót: {Hotkey}", settings.Hotkey.DisplayText);

        var meetingAutomation = _services.GetRequiredService<IMeetingAutomationService>();
        _ = meetingAutomation.StartAsync();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddInfrastructure();
        services.AddAudioServices();

        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<INotificationService, TrayNotificationService>();
        services.AddSingleton<IMeetingAutomationService, MeetingAutomationService>();

        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<TrayViewModel>(sp =>
            new TrayViewModel(
                sp.GetRequiredService<IRecordingCoordinator>(),
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<INotificationService>(),
                sp.GetRequiredService<ILogger<TrayViewModel>>(),
                OpenSettings));
    }

    private void OpenSettings()
    {
        void Apply()
        {
            if (_settingsWindow is not null)
            {
                _settingsWindow.Activate();
                _settingsWindow.WindowState = WindowState.Normal;
                return;
            }

            var vm = _services!.GetRequiredService<SettingsViewModel>();
            _settingsWindow = new SettingsWindow(vm);
            _settingsWindow.Closed += (_, _) =>
            {
                vm.Dispose();
                _settingsWindow = null;
            };
            _settingsWindow.Show();
        }

        if (Dispatcher.CheckAccess())
            Apply();
        else
            _ = Dispatcher.InvokeAsync(Apply);
    }

    private void OnDeviceChanged(object? sender, DeviceChangedEventArgs e)
    {
        _logger?.LogInformation("DeviceChanged {Kind} {Id}", e.Kind, e.DeviceId);

        var coordinator = _services?.GetService<IRecordingCoordinator>();
        if (coordinator is null)
            return;

        if (coordinator.State != AppRecordingState.Recording)
            return;

        var session = coordinator.CurrentSession;
        if (session is null)
            return;

        var activeMic = session.MicrophoneDeviceId;
        var activeOut = session.OutputDeviceId;

        if (e.Kind is DeviceChangeKind.Removed or DeviceChangeKind.StateChanged
            && (e.DeviceId == activeMic || e.DeviceId == activeOut))
        {
            // Sprawdź czy urządzenie nadal aktywne
            var devices = _services!.GetRequiredService<IAudioDeviceService>();
            var stillThere = devices.FindDeviceById(e.DeviceId);
            if (stillThere is null)
            {
                var which = e.DeviceId == activeMic ? "mikrofon" : "urządzenie wyjściowe (słuchawki)";
                _logger?.LogWarning("Utracono {Which} podczas nagrywania", which);
                _services?.GetService<INotificationService>()?.ShowError(
                    "Utracono urządzenie",
                    $"Odłączono {which}. Nagrywanie zostanie zakończone, a nagrany materiał zachowany.");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await coordinator.StopRecordingAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Stop po utracie urządzenia");
                    }
                });
            }
            else if (e.Message?.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) == true
                     || e.Kind == DeviceChangeKind.StateChanged)
            {
                // Możliwa zmiana profilu BT — informuj, ale nie przełączaj
                _services?.GetService<INotificationService>()?.ShowWarning(
                    "Zmiana urządzenia audio",
                    "Wykryto zmianę stanu urządzenia (np. profil Bluetooth). Sprawdź, czy nagranie nadal działa poprawnie.");
            }
        }
    }

    private void WarnIfDevicesMissing(AppSettings settings, IAudioDeviceService devices)
    {
        var mic = devices.ResolveDevice(settings.MicrophoneDeviceId, AudioDeviceType.Capture);
        if (mic.UsedFallback && mic.WarningMessage is not null)
        {
            _logger?.LogWarning(mic.WarningMessage);
            if (mic.Device is not null)
            {
                settings.MicrophoneDeviceId = mic.Device.Id;
                _services!.GetRequiredService<ISettingsService>().Save(settings);
            }
            MessageBox.Show(mic.WarningMessage, "Mikrofon", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        var output = devices.ResolveDevice(settings.OutputDeviceId, AudioDeviceType.Render);
        if (output.UsedFallback && output.WarningMessage is not null)
        {
            _logger?.LogWarning(output.WarningMessage);
            if (output.Device is not null)
            {
                settings.OutputDeviceId = output.Device.Id;
                _services!.GetRequiredService<ISettingsService>().Save(settings);
            }
            MessageBox.Show(output.WarningMessage, "Urządzenie wyjściowe", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            _hotkeyService?.Dispose();
            _tray?.Dispose();

            var meetingAutomation = _services?.GetService<IMeetingAutomationService>();
            if (meetingAutomation is not null)
                await meetingAutomation.StopAsync();

            var coordinator = _services?.GetService<IRecordingCoordinator>();
            if (coordinator is not null)
                await coordinator.DisposeAsync();

            _services?.GetService<IAudioDeviceService>()?.Dispose();
            _singleInstance?.Dispose();
            if (_services is not null)
                await _services.DisposeAsync();
            SerilogSetup.CloseAndFlush();
        }
        catch
        {
            // ignore shutdown errors
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "DispatcherUnhandledException");
        MessageBox.Show(
            "Wystąpił nieoczekiwany błąd. Szczegóły zapisano w logu.\n\n" + e.Exception.Message,
            "Błąd", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            _logger?.LogCritical(ex, "UnhandledException");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }
}
