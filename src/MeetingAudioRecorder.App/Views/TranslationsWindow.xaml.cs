using System.Windows;
using System.Windows.Controls;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Infrastructure.Translation;

namespace MeetingAudioRecorder.App.Views;

public partial class TranslationsWindow : Window
{
    private readonly TranslationStore store;
    private readonly Func<Guid?> active;
    private IReadOnlyList<TranslationSession> sessions = [];
    private IReadOnlyList<TranslationCaption> captions = [];
    private int loadVersion;
    public sealed record Row(string Label, string English, string Polish);
    public TranslationsWindow(TranslationStore store, Func<Guid?> active)
    {
        InitializeComponent(); this.store = store; this.active = active; Loaded += async (_, _) => await Reload();
    }
    internal void SelectFirstForDiagnostic() => Sessions.SelectedIndex = 0;
    private async Task Reload()
    {
        try { sessions = await Task.Run(store.List); Sessions.ItemsSource = sessions; Status.Text = $"Sesje: {sessions.Count}. Zaznacz komórki i użyj Ctrl+C, aby skopiować."; }
        catch (Exception ex) { Status.Text = "Błąd odczytu historii: " + ex.Message; }
    }
    private async void Refresh(object sender, RoutedEventArgs e) => await Reload();
    private async void SelectedSession(object sender, SelectionChangedEventArgs e)
    {
        int version = ++loadVersion; captions = []; Captions.ItemsSource = null;
        if (Sessions.SelectedItem is not TranslationSession session) return;
        try
        {
            var document = await Task.Run(() => store.Load(session.Id)); if (version != loadVersion) return;
            captions = document.Captions;
            var state = session.State == "running" ? (active() == session.Id ? "W trakcie" : "Przerwana — zachowano zapisane frazy") : session.StateLabel;
            TitleText.Text = session.Title + " · " + state;
            Status.Text = $"Zapisane frazy: {captions.Count}. Nieczytelne fragmenty: {document.UnreadableSegments}. Tylko zapis lokalny."; FilterCaptions();
        }
        catch (Exception ex) { Status.Text = "Nie można odczytać sesji: " + ex.Message; }
    }
    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (Sessions is null) return;

        FilterCaptions();
    }
    private void SessionSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (Sessions is not null) Sessions.ItemsSource = sessions.Where(s => s.Title.Contains(SessionSearch.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    private void FilterCaptions() => Captions.ItemsSource = captions.Where(c => c.English.Contains(Search.Text, StringComparison.OrdinalIgnoreCase) || c.Polish.Contains(Search.Text, StringComparison.OrdinalIgnoreCase)).Select(c => new Row($"{TimeSpan.FromMilliseconds(c.StartMs):hh\\:mm\\:ss}\n{(c.Source == "output" ? "Wyjście" : "Mikrofon")}", c.English, c.Polish)).ToArray();
}
