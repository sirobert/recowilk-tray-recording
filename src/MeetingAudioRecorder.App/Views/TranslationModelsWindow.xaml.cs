using System.Net.Http;
using System.Windows;
using MeetingAudioRecorder.Infrastructure.Translation;

namespace MeetingAudioRecorder.App.Views;

public partial class TranslationModelsWindow : Window
{
    private readonly string directory;
    private readonly bool turbo;
    private CancellationTokenSource? cancellation;
    public TranslationModelsWindow(string directory, bool turbo)
    {
        InitializeComponent(); this.directory = directory; this.turbo = turbo;
        var mb = TranslationModelDownloader.ForProfile(turbo).Sum(f => f.Bytes) / 1_000_000.0;
        Description.Text = $"Whisper rozpoznaje mowę, Marian tłumaczy ją na polski. Marian jest dołączony do programu. Whisper i Silero pobierzemy z Hugging Face / GitHub (maksymalnie {mb:N0} MB). Poprawne pliki zostaną użyte ponownie. Po przygotowaniu modeli tłumaczenie działa lokalnie, bez wysyłania rozmów.";
        Location.Text = "Folder: " + directory;
        Closing += (_, e) => { if (cancellation is not null) { e.Cancel = true; cancellation.Cancel(); Status.Text = "Anulowanie…"; } };
    }
    private async void Start(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false; cancellation = new CancellationTokenSource();
        try
        {
            var token = cancellation.Token;
            Progress.IsIndeterminate = true; Status.Text = "Przygotowanie i sprawdzanie modelu Marian…";
            await Task.Run(() => BundledTranslationModels.InstallAsync(directory, token), token);
            Progress.IsIndeterminate = false;
            using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromHours(2) };
            var progress = new Progress<ModelDownloadProgress>(p =>
            {
                Progress.Value = p.Total == 0 ? 0 : 100.0 * p.Completed / p.Total;
                Status.Text = $"{p.Stage}: {p.File}\n{p.Completed / 1_000_000.0:N1} / {p.Total / 1_000_000.0:N1} MB";
            });
            await Task.Run(() => new TranslationModelDownloader(client).EnsureAsync(directory, TranslationModelDownloader.ForProfile(turbo), progress, token), token);
            cancellation.Dispose(); cancellation = null; DialogResult = true;
        }
        catch (OperationCanceledException) { Status.Text = "Anulowano. Ukończone modele pozostają na dysku. Nieukończony plik zostanie pobrany ponownie."; }
        catch (Exception ex) { Status.Text = "Nie udało się przygotować modeli: " + ex.Message + "\nMożesz spróbować ponownie."; }
        finally { cancellation?.Dispose(); cancellation = null; StartButton.IsEnabled = true; Progress.IsIndeterminate = false; }
    }
    private void Cancel(object sender, RoutedEventArgs e)
    {
        if (cancellation is not null) { cancellation.Cancel(); Status.Text = "Anulowanie…"; }
        else Close();
    }
}
