using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MeetingAudioRecorder.App.Views;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Infrastructure.Translation;

namespace MeetingAudioRecorder.App.Services;

// Explicit isolated diagnostics: bypass startup, credentials, capture devices and upload services.
internal static class TranslationDiagnostics
{
    internal static async Task RunAsync(string[] args)
    {
        try
        {
            if (args[0] == "--translation-model-smoke" && args.Length == 2)
            {
                var dir = Path.GetFullPath(args[1]); Directory.CreateDirectory(dir);
                using var client = new System.Net.Http.HttpClient();
                var files = TranslationModelDownloader.ForProfile(false).Take(1).ToArray();
                await new TranslationModelDownloader(client).EnsureAsync(dir, files, null, CancellationToken.None);
                await BundledTranslationModels.InstallAsync(dir, CancellationToken.None);
                var verified = await TranslationModelDownloader.IsValidAsync(Path.Combine(dir, files[0].Path), files[0], CancellationToken.None);
                foreach (var file in BundledTranslationModels.Files()) verified &= await TranslationModelDownloader.IsValidAsync(Path.Combine(dir, file.Path), file, CancellationToken.None);
                await File.WriteAllTextAsync(Path.Combine(dir, "result.json"), JsonSerializer.Serialize(new { verified, downloaded = files[0].Bytes, bundledFiles = BundledTranslationModels.Files().Count }));
                Application.Current.Shutdown(verified ? 0 : 1); return;
            }
            if (args[0] == "--translation-smoke" && args.Length >= 4)
            {
                var report = Path.GetFullPath(args[3]); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
                var store = new TranslationStore(Path.Combine(Path.GetDirectoryName(report)!, "isolated-translation-store"));
                var session = new TranslationSession { Title = "Diagnostic WAV" }; store.Create(session);
                int drafts = 0, finals = 0, ready = 0; bool ordered = true;
                var lastFinal = new Dictionary<string, int>(); var revisions = new Dictionary<string, int>();
                var events = new List<object>(); var clock = new System.Diagnostics.Stopwatch(); var gate = new object();
                var sources = args.ElementAtOrDefault(5) == "both" ? new[] { "output", "microphone" } : new[] { "output" };
                using var stop = new CancellationTokenSource(); Task? timer = null;
                string? error = null;
                try
                {
                    var executable = Path.Combine(AppContext.BaseDirectory, "TranslationWorker", "MeetingAudioRecorder.TranslationWorker.exe");
                    await Task.WhenAll(sources.Select(source => Task.Run(() => new LocalTranslationWorker(executable).RunAsync(new(args[1], false, source, "", args[2]), value =>
                    {
                        lock (gate)
                        {
                            if (value.Kind == "ready")
                            {
                                clock.Start(); ready++;
                                if (ready == sources.Length && args.Length > 4 && int.TryParse(args[4], out int ms) && ms > 0) timer = Task.Run(async () => { await Task.Delay(ms); stop.Cancel(); });
                            }
                            if (value.Caption is not { } c) return;
                            if (c.IsDraft) { drafts++; var key = c.Source + ":" + c.Id; revisions[key] = revisions.GetValueOrDefault(key) + 1; ordered &= c.Id > lastFinal.GetValueOrDefault(c.Source); }
                            else { finals++; ordered &= c.Id > lastFinal.GetValueOrDefault(c.Source); lastFinal[c.Source] = c.Id; }
                            store.Append(session.Id, c); events.Add(new { c.Source, c.Id, c.IsDraft, c.StartMs, c.EndMs, elapsedSeconds = clock.Elapsed.TotalSeconds });
                        }
                    }, stop.Token))));
                    store.Complete(session.Id, "completed");
                }
                catch (Exception ex) { error = ex.Message; store.Complete(session.Id, "error"); }
                if (timer is not null) await timer;
                var loaded = new TranslationStore(Path.Combine(Path.GetDirectoryName(report)!, "isolated-translation-store")).Load(session.Id);
                bool passed = error is null && finals > 0 && drafts > 0 && ordered && loaded.Captions.Count == finals && loaded.Captions.All(c => !c.IsDraft) && sources.All(source => loaded.Captions.Any(c => c.Source == source));
                await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { passed, drafts, finals, ordered, error, revisions, sources, events, reloadedFinals = loaded.Captions.Count }, new JsonSerializerOptions { WriteIndented = true }));
                Application.Current.Shutdown(passed ? 0 : 1); return;
            }
            if (args[0] == "--translation-ui-smoke" && args.Length == 2)
            {
                var dir = Path.GetFullPath(args[1]); Directory.CreateDirectory(dir);
                var models = new TranslationModelsWindow(Path.Combine(dir, "Models"), false); models.Show(); await Task.Delay(150); await Render(models, Path.Combine(dir, "models.png")); models.Close();
                var settings = new TranslationSettings { ShowOriginal = true };
                var overlay = new TranslationOverlay(settings, () => { }, _ => { }); overlay.Show();
                overlay.Status("Wyjście audio · tłumaczenie lokalne");
                overlay.Add(new("output", 1, "We will review the results tomorrow.", "Jutro omówimy wyniki.", false, 100, 2000, "text"));
                overlay.Add(new("output", 2, "We are currently working on", "Obecnie pracujemy nad", true, 2100, 4000, "text"));
                await Render(overlay, Path.Combine(dir, "overlay.png")); overlay.Destroy();
                var config = new TranslationSettingsWindow(settings, _ => { }); config.Show(); await Render(config, Path.Combine(dir, "settings.png")); config.Close();
                var store = new TranslationStore(Path.Combine(dir, "synthetic-history")); var session = new TranslationSession { Title = "Przegląd wyników — przykład" }; store.Create(session);
                store.Append(session.Id, new("output", 1, "We will review the results tomorrow.", "Jutro omówimy wyniki.", false, 100, 2000, "text")); store.Complete(session.Id, "completed");
                var history = new TranslationsWindow(store, () => null); history.Show(); await Task.Delay(300); history.SelectFirstForDiagnostic(); await Task.Delay(300); await Render(history, Path.Combine(dir, "history.png")); history.Close();
                Application.Current.Shutdown(0); return;
            }
            Application.Current.Shutdown(2);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.GetType().Name); Application.Current.Shutdown(1); }
    }
    private static async Task Render(Window window, string path)
    {
        await Task.Delay(250); window.UpdateLayout();
        var image = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
