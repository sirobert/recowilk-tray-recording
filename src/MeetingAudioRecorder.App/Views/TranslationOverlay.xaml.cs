using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.App.Views;

public partial class TranslationOverlay : Window
{
    public sealed record Line(string Source, int Id, bool Draft, string Label, string Polish, string English, Visibility OriginalVisibility);
    private readonly ObservableCollection<Line> lines = [];
    private readonly Dictionary<string, int> finalized = [];
    private readonly Action stop;
    private readonly Action<TranslationOverlay> geometry;
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool showOriginal, allowClose, applying, finished, stopRequested;
    public TranslationOverlay(TranslationSettings settings, Action stop, Action<TranslationOverlay> geometry)
    {
        InitializeComponent(); this.stop = stop; this.geometry = geometry; Lines.ItemsSource = lines;
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
            for (int i = 0; i < lines.Count; i++) lines[i] = lines[i] with { OriginalVisibility = showOriginal ? Visibility.Visible : Visibility.Collapsed };
        }
        finally { applying = false; }
    }
    public void Add(TranslationCaption caption)
    {
        if (caption.IsDraft && finalized.GetValueOrDefault(caption.Source) >= caption.Id) return;
        foreach (var old in lines.Where(x => x.Source == caption.Source && (x.Draft || x.Id == caption.Id)).ToArray()) lines.Remove(old);
        if (!caption.IsDraft) finalized[caption.Source] = caption.Id;
        string source = caption.Source == "output" ? "Rozmówcy · wyjście audio" : "Mikrofon";
        lines.Add(new(caption.Source, caption.Id, caption.IsDraft, source + (caption.IsDraft ? " · wersja robocza" : ""), caption.Polish, caption.English, showOriginal ? Visibility.Visible : Visibility.Collapsed));
        while (lines.Count > 6) lines.RemoveAt(0);
        Scroll.ScrollToEnd();
    }
    public void Status(string text) => StatusLabel.Text = text;
    public void Finish(string status)
    {
        finished = true; StopButton.Content = "Zamknij";
        foreach (var row in lines.Where(x => x.Draft).ToArray()) lines.Remove(row);
        Status(status);
    }
    public void Destroy() { saveTimer.Stop(); allowClose = true; Close(); }
    private void ScheduleSave() { if (!applying && IsLoaded) { saveTimer.Stop(); saveTimer.Start(); } }
    private void MoveWindow(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void HideWindow(object sender, RoutedEventArgs e) => Hide();
    private void StopTranslation(object sender, RoutedEventArgs e) => Dismiss();
    private void Dismiss()
    {
        Hide();
        if (!finished && !stopRequested) { stopRequested = true; stop(); }
    }
    protected override void OnClosing(CancelEventArgs e) { if (!allowClose) { e.Cancel = true; Dismiss(); } base.OnClosing(e); }
}
