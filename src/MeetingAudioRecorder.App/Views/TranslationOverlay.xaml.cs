using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.App.Views;

public partial class TranslationOverlay : Window
{
    public sealed record Line(string Source, int Id, bool Draft, string Label, string Polish, string English, Visibility OriginalVisibility);
    private readonly ObservableCollection<Line> lines = [];
    private readonly Dictionary<Line, Paragraph> paragraphs = [];
    private readonly Dictionary<string, int> finalized = [];
    private readonly Action stop;
    private readonly Action<TranslationOverlay> geometry;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool showOriginal, allowClose, applying, finished, stopRequested, preparing;
    public TranslationOverlay(TranslationSettings settings, Action stop, Action<TranslationOverlay> geometry)
    {
        InitializeComponent(); this.stop = stop; this.geometry = geometry;
        Apply(settings);
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); geometry(this); };
        LocationChanged += (_, _) => ScheduleSave(); SizeChanged += (_, _) => ScheduleSave();
    }
    public void Apply(TranslationSettings settings)
    {
        applying = true;
        try
        {
            FontFamily = new FontFamily(settings.FontFamily); FontSize = settings.FontSize;
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.Foreground));
            var color = (Color)ColorConverter.ConvertFromString(settings.Background); color.A = (byte)(255 * settings.BackgroundOpacity);
            Surface.Background = new SolidColorBrush(color); showOriginal = settings.ShowOriginal;
            Width = double.IsFinite(settings.Width) ? Math.Clamp(settings.Width, 340, Math.Max(340, SystemParameters.VirtualScreenWidth)) : 660;
            Height = double.IsFinite(settings.Height) ? Math.Clamp(settings.Height, 120, Math.Max(120, SystemParameters.VirtualScreenHeight)) : 220;
            Left = double.IsFinite(settings.Left) ? Math.Clamp(settings.Left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width)) : 80;
            Top = double.IsFinite(settings.Top) ? Math.Clamp(settings.Top, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height)) : 80;
            foreach (var line in lines) ((Run)paragraphs[line].Inlines.LastInline).Text = showOriginal ? "\n" + line.English : "";
        }
        finally { applying = false; }
    }
    public void Add(TranslationCaption caption)
    {
        if (caption.IsDraft && finalized.GetValueOrDefault(caption.Source) >= caption.Id) return;
        var selectionStart = Transcript.Selection.Start.GetPositionAtOffset(0, LogicalDirection.Backward);
        var selectionEnd = Transcript.Selection.End.GetPositionAtOffset(0, LogicalDirection.Backward);
        bool selected = !Transcript.Selection.IsEmpty;
        bool follow = Transcript.Selection.IsEmpty && !Transcript.IsMouseCaptureWithin
            && Transcript.ExtentHeight - Transcript.ViewportHeight - Transcript.VerticalOffset <= 2;
        foreach (var old in lines.Where(x => x.Source == caption.Source && (x.Draft || x.Id == caption.Id)).ToArray()) RemoveLine(old);
        if (!caption.IsDraft) finalized[caption.Source] = caption.Id;
        string source = caption.Source == "output" ? "Rozmówcy · wyjście audio" : "Mikrofon";
        var line = new Line(caption.Source, caption.Id, caption.IsDraft, source + (caption.IsDraft ? " · wersja robocza" : ""), caption.Polish, caption.English, showOriginal ? Visibility.Visible : Visibility.Collapsed);
        lines.Add(line);
        var paragraph = new Paragraph { Margin = new Thickness(0, 3, 0, 7) };
        paragraph.Inlines.Add(new Run(line.Label) { FontSize = 10 });
        paragraph.Inlines.Add(new LineBreak());
        paragraph.Inlines.Add(new Run(line.Polish));
        paragraph.Inlines.Add(new Run(showOriginal ? "\n" + line.English : ""));
        paragraphs.Add(line, paragraph); Transcript.Document.Blocks.Add(paragraph);
        if (selected) Transcript.Selection.Select(selectionStart, selectionEnd);
        if (follow) Transcript.ScrollToEnd();
    }
    private void RemoveLine(Line line)
    {
        Transcript.Document.Blocks.Remove(paragraphs[line]); paragraphs.Remove(line); lines.Remove(line);
    }
    private void CopyAll(object sender, RoutedEventArgs e)
    {
        var text = new TextRange(Transcript.Document.ContentStart, Transcript.Document.ContentEnd).Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show("Schowek jest zajęty. Spróbuj skopiować ponownie.", "Kopiowanie", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    public void Status(string text) { if (!stopRequested && !finished) StatusLabel.Text = text; }
    public void BeginPreparing()
    {
        preparing = true; StopButton.IsEnabled = false; StopButton.Content = "Przygotowanie…";
        BusyIndicator.Visibility = Visibility.Visible;
        StatusLabel.Text = "Sprawdzam modele tłumaczenia…";
        Show();
    }
    public void BeginRunning()
    {
        preparing = false; StopButton.IsEnabled = true; StopButton.Content = "■ Stop";
        BusyIndicator.Visibility = Visibility.Collapsed;
        StatusLabel.Text = "Ładowanie tłumacza…";
    }
    public void BeginStopping()
    {
        stopRequested = true; StopButton.IsEnabled = false; StopButton.Content = "Kończenie…";
        BusyIndicator.Visibility = Visibility.Visible;
        StatusLabel.Text = "Kończę frazę i zapisuję… Poczekaj na zakończenie przed ponownym startem.";
        if (!IsVisible) Show();
    }
    public void Finish(string status)
    {
        finished = true; preparing = false; StopButton.Content = "Zamknij"; StopButton.IsEnabled = true;
        BusyIndicator.Visibility = Visibility.Collapsed;
        foreach (var row in lines.Where(x => x.Draft).ToArray()) RemoveLine(row);
        StatusLabel.Text = status;
    }
    public void Destroy() { saveTimer.Stop(); allowClose = true; Close(); }
    private void ScheduleSave() { if (!applying && IsLoaded) { saveTimer.Stop(); saveTimer.Start(); } }
    private void MoveWindow(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void HideWindow(object sender, RoutedEventArgs e) => Hide();
    private void StopTranslation(object sender, RoutedEventArgs e) => Dismiss();
    private void Dismiss()
    {
        if (finished) { Hide(); return; }
        if (preparing || stopRequested) return;
        BeginStopping(); stop();
    }
    protected override void OnClosing(CancelEventArgs e) { if (!allowClose) { e.Cancel = true; Dismiss(); } base.OnClosing(e); }
}
