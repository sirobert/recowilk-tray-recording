using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetingAudioRecorder.App.Views;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using MeetingAudioRecorder.Infrastructure.Translation;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.App.Services;

public partial class TranslationController : ObservableObject
{
    private readonly ISettingsService settings;
    private readonly TranslationStore store = new();
    private readonly HotkeyService hotkey;
    private TranslationOverlay? overlay;
    private CancellationTokenSource? stop;
    private Task? running;
    private bool shuttingDown, starting, stopping;
    private int generation;
    private Guid? activeId;
    [ObservableProperty] private string menuText = "Rozpocznij tłumaczenie";
    [ObservableProperty] private TranslationActivity activity = new();
    public TranslationController(ISettingsService settings, ILogger<HotkeyService> logger, IntPtr hwnd)
    {
        this.settings = settings; hotkey = new HotkeyService(logger, 0xBEE3); hotkey.Attach(hwnd);
        hotkey.HotkeyPressed += async (_, _) => await ToggleAsync();
        if (!hotkey.Register(settings.Current.Translation.Hotkey))
            MessageBox.Show(hotkey.LastError, "Skrót tłumaczenia", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleAsync()
    {
        if (shuttingDown) return;
        if (starting || stopping) { overlay?.Show(); return; }
        if (running is not null) { await StopAsync(); return; }
        starting = true; Activity = new(TranslationActivityState.Starting);
        try
        {
            var snapshot = settings.Current;
            var config = snapshot.Translation;
            Activity = new(TranslationActivityState.Starting, config.OutputEnabled ? (config.MicrophoneEnabled ? "wyjście + mikrofon" : "wyjście") : "mikrofon");
            var validation = SettingsValidator.Validate(snapshot);
            if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
            var executable = Path.Combine(AppContext.BaseDirectory, "TranslationWorker", "MeetingAudioRecorder.TranslationWorker.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("Brak silnika tłumaczenia. Zainstaluj kompletny pakiet Recordera.");
            overlay?.Destroy(); overlay = new TranslationOverlay(config, () => StopCommand.Execute(null), SaveGeometry);
            overlay.BeginPreparing(); MenuText = "Przygotowanie tłumaczenia…";
            if (!await Task.Run(() => BundledTranslationModels.IsReadyAsync(config.ModelsDirectory, config.Turbo, CancellationToken.None)))
                if (new TranslationModelsWindow(config.ModelsDirectory, config.Turbo) { Owner = overlay }.ShowDialog() != true)
                {
                    overlay.Finish("Przygotowanie anulowane. Możesz ponowić start skrótem lub z menu."); return;
                }
            if (shuttingDown) return;
            if ((config.OutputEnabled && string.IsNullOrWhiteSpace(snapshot.OutputDeviceId)) || (config.MicrophoneEnabled && string.IsNullOrWhiteSpace(snapshot.MicrophoneDeviceId))) throw new InvalidOperationException("Wybierz i zapisz urządzenia w ustawieniach Recordera.");
            var session = new TranslationSession
            {
                Title = "Tłumaczenie " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Sources = config.OutputEnabled ? (config.MicrophoneEnabled ? "output,microphone" : "output") : "microphone",
                MicrophoneDeviceId = snapshot.MicrophoneDeviceId,
                OutputDeviceId = snapshot.OutputDeviceId,
                Profile = config.Turbo ? "turbo Q8 / Marian int8" : "small.en Q8 / Marian int8"
            };
            await Task.Run(() => store.Create(session));
            stop = new(); activeId = session.Id; int current = ++generation;
            overlay.BeginRunning();
            MenuText = "Zatrzymaj tłumaczenie";
            running = RunSessionAsync(session, snapshot, executable, current, stop);
            _ = ObserveSessionAsync(running, current);
        }
        catch (Exception ex)
        {
            Activity = Activity with { State = TranslationActivityState.Error, Detail = ex.Message };
            overlay?.Finish("Nie udało się uruchomić: " + ex.Message);
            MessageBox.Show(ex.Message, "Tłumaczenie", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            starting = false;
            if (running is null)
            {
                MenuText = "Rozpocznij tłumaczenie";
                if (Activity.State == TranslationActivityState.Starting) Activity = new();
            }
        }
    }
    private async Task RunSessionAsync(TranslationSession session, AppSettings snapshot, string executable, int current, CancellationTokenSource cancellation)
    {
        var clock = Stopwatch.StartNew(); int failed = 0, ready = 0;
        int expected = (snapshot.Translation.OutputEnabled ? 1 : 0) + (snapshot.Translation.MicrophoneEnabled ? 1 : 0);
        async Task Source(string source, string device)
        {
            double offset = 0; bool sourceReady = false;
            try
            {
                await new LocalTranslationWorker(executable).RunAsync(new(snapshot.Translation.ModelsDirectory, snapshot.Translation.Turbo, source, device), e =>
                {
                    if (e.Kind == "ready")
                    {
                        offset = clock.Elapsed.TotalMilliseconds;
                        if (!sourceReady)
                        {
                            sourceReady = true;
                            if (Interlocked.Increment(ref ready) == expected) Post(current, () =>
                            {
                                if (Activity.State == TranslationActivityState.Starting && !stopping)
                                    Activity = Activity with { State = TranslationActivityState.Running };
                            });
                        }
                    }
                    if (e.Caption is { } caption)
                    {
                        var timed = caption with { StartMs = caption.StartMs + offset, EndMs = caption.EndMs + offset };
                        store.Append(session.Id, timed);
                        Post(current, () => overlay?.Add(timed));
                    }
                    else if (e.Kind == "status") Post(current, () => overlay?.Status((source == "output" ? "Wyjście: " : "Mikrofon: ") + e.Message));
                }, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref failed, 1); cancellation.Cancel();
                Post(current, () => { Activity = Activity with { State = TranslationActivityState.Error, Detail = ex.Message }; overlay?.Status("Błąd: " + ex.Message); });
                throw;
            }
        }
        try
        {
            var sources = new List<Task>();
            if (snapshot.Translation.OutputEnabled) sources.Add(Task.Run(() => Source("output", snapshot.OutputDeviceId)));
            if (snapshot.Translation.MicrophoneEnabled) sources.Add(Task.Run(() => Source("microphone", snapshot.MicrophoneDeviceId)));
            await Task.WhenAll(sources);
        }
        finally { await Task.Run(() => store.Complete(session.Id, failed != 0 ? "error" : "completed")); }
    }
    private async Task ObserveSessionAsync(Task task, int current)
    {
        string result = "Zatrzymano · zapisano lokalnie. Możesz uruchomić tłumaczenie ponownie.";
        string? error = null;
        try { await task; }
        catch (Exception ex) { error = ex.Message; result = "Przerwano: " + ex.Message; }
        await Application.Current.Dispatcher.InvokeAsync(() => { });
        if (current != generation) return;
        Activity = error is null ? new() : Activity with { State = TranslationActivityState.Error, Detail = error };
        overlay?.Finish(result); MenuText = "Rozpocznij tłumaczenie";
        running = null; stopping = false; activeId = null; stop?.Dispose(); stop = null;
    }
    private void Post(int current, Action action) => Application.Current.Dispatcher.BeginInvoke(new Action(() => { if (current == generation) action(); }));
    [RelayCommand]
    public async Task StopAsync()
    {
        var task = running; if (task is null) return;
        stopping = true;
        if (Activity.State != TranslationActivityState.Error) Activity = Activity with { State = TranslationActivityState.Stopping };
        MenuText = "Kończenie tłumaczenia…"; overlay?.BeginStopping(); stop?.Cancel();
        try { await task; } catch { /* Observer displays error and keeps saved segments. */ }
    }
    [RelayCommand] private void Show() { overlay?.Show(); }
    [RelayCommand] public void OpenSettings() => new TranslationSettingsWindow(settings.Current.Translation, SaveSettings).ShowDialog();
    [RelayCommand] private void OpenHistory() => new TranslationsWindow(store, () => activeId).Show();
    private void SaveSettings(TranslationSettings candidate)
    {
        var next = settings.Current; next.Translation = candidate;
        var validation = SettingsValidator.Validate(next);
        if (!validation.IsValid) throw new InvalidOperationException(string.Join(" ", validation.Errors));
        var result = SettingsTransactionService.TryCommit(settings, hotkey, next, translation: true);
        if (!result.Success) throw new InvalidOperationException(result.ErrorMessage);
        overlay?.Apply(candidate);
    }
    private void SaveGeometry(TranslationOverlay window)
    {
        try
        {
            var next = settings.Current; var t = next.Translation;
            t.Left = window.Left; t.Top = window.Top; t.Width = window.Width; t.Height = window.Height;
            settings.Save(next);
        }
        catch { window.Status("Nie udało się zapisać położenia okna."); }
    }
    public async Task ShutdownAsync()
    {
        shuttingDown = true; await StopAsync(); overlay?.Destroy(); overlay = null; hotkey.Dispose();
    }
}
