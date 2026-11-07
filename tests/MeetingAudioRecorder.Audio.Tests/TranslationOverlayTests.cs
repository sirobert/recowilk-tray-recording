using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MeetingAudioRecorder.App.Views;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Audio.Tests;

public class TranslationOverlayTests
{
    [Fact]
    public async Task EntireSessionCanBeSelectedAcrossCaptions()
    {
        await OnSta(() =>
        {
            var window = new TranslationOverlay(new TranslationSettings { ShowOriginal = true }, () => { }, _ => { });
            try
            {
                for (int i = 1; i <= 30; i++) window.Add(new("output", i, $"English {i}", $"Polski {i}", false, 0, 100, "text"));
                var transcript = Assert.IsType<RichTextBox>(window.FindName("Transcript"));
                Assert.True(transcript.IsReadOnly);
                transcript.SelectAll();
                Assert.Contains("Polski 1\n", transcript.Selection.Text.Replace("\r\n", "\n"));
                Assert.Contains("English 30", transcript.Selection.Text);
                string selected = transcript.Selection.Text;
                window.Add(new("output", 31, "Next", "Następny", false, 0, 100, "text"));
                // WPF may omit the trailing paragraph separator when the old document end becomes an interior boundary.
                Assert.Equal(selected.TrimEnd(), transcript.Selection.Text.TrimEnd());
            }
            finally { window.Destroy(); }
        });
    }

    [Fact]
    public async Task DraftUpdatesDoNotDuplicateOrRemoveEarlierFinals()
    {
        await OnSta(() =>
        {
            var window = new TranslationOverlay(new TranslationSettings(), () => { }, _ => { });
            try
            {
                window.Add(new("output", 1, "First", "Pierwszy", false, 0, 100, "text"));
                window.Add(new("output", 2, "Draft", "Szkic", true, 100, 200, "text"));
                window.Add(new("output", 2, "Final", "Gotowy", false, 100, 200, "text"));
                window.Add(new("output", 2, "Late", "Spóźniony", true, 100, 200, "text"));
                var transcript = Assert.IsType<RichTextBox>(window.FindName("Transcript"));
                transcript.SelectAll();
                Assert.Contains("Pierwszy", transcript.Selection.Text);
                Assert.Contains("Gotowy", transcript.Selection.Text);
                Assert.DoesNotContain("Szkic", transcript.Selection.Text);
                Assert.DoesNotContain("Spóźniony", transcript.Selection.Text);
                Assert.DoesNotContain("First", transcript.Selection.Text);
                window.Apply(new TranslationSettings { ShowOriginal = true });
                string text = new TextRange(transcript.Document.ContentStart, transcript.Document.ContentEnd).Text;
                Assert.Contains("First", text); Assert.Contains("Final", text);
            }
            finally { window.Destroy(); }
        });
    }

    [Fact]
    public async Task NewCaptionDoesNotMoveReaderAwayFromBeginning()
    {
        await OnSta(() =>
        {
            var window = new TranslationOverlay(new TranslationSettings(), () => { }, _ => { });
            try
            {
                window.Show();
                for (int i = 1; i <= 100; i++) window.Add(new("output", i, "English", $"Wypowiedź numer {i}", false, 0, 100, "text"));
                var transcript = Assert.IsType<RichTextBox>(window.FindName("Transcript"));
                window.UpdateLayout(); transcript.ScrollToHome(); window.UpdateLayout();
                var scrollbar = VisualChildren(transcript).OfType<System.Windows.Controls.Primitives.ScrollBar>()
                    .Single(bar => bar.Orientation == Orientation.Vertical);
                Assert.Equal(8, scrollbar.ActualWidth);
                window.Add(new("output", 101, "Next", "Następny", false, 0, 100, "text"));
                window.UpdateLayout();
                Assert.Equal(0, transcript.VerticalOffset);
            }
            finally { window.Destroy(); }
        });
    }

    [Fact]
    public async Task PreparationIsVisibleBeforeWorkerStarts()
    {
        await OnSta(() =>
        {
            var window = new TranslationOverlay(new TranslationSettings(), () => { }, _ => { });
            try
            {
                window.BeginPreparing();
                Assert.True(window.IsVisible);
                Assert.False(((Button)window.FindName("StopButton")).IsEnabled);
                Assert.Equal(Visibility.Visible, ((ProgressBar)window.FindName("BusyIndicator")).Visibility);
                Assert.Contains("Sprawdzam modele", ((TextBlock)window.FindName("StatusLabel")).Text);
                window.BeginRunning();
                Assert.True(((Button)window.FindName("StopButton")).IsEnabled);
                window.Status("Nasłuchiwanie");
                Assert.Equal("Nasłuchiwanie", ((TextBlock)window.FindName("StatusLabel")).Text);
            }
            finally { window.Destroy(); }
        });
    }

    [Fact]
    public async Task StopFromHiddenWindowRevealsProgressUntilCompletion()
    {
        await OnSta(() =>
        {
            var window = new TranslationOverlay(new TranslationSettings(), () => { }, _ => { });
            try
            {
                window.Show(); window.Hide(); window.BeginStopping();
                Assert.True(window.IsVisible);
                Assert.Equal(Visibility.Visible, ((ProgressBar)window.FindName("BusyIndicator")).Visibility);
                window.Hide(); window.Show();
                Assert.Contains("Kończę", ((TextBlock)window.FindName("StatusLabel")).Text);
                window.Finish("Zatrzymano"); window.Status("Opóźniony status");
                Assert.Equal("Zatrzymano", ((TextBlock)window.FindName("StatusLabel")).Text);
                Assert.Equal(Visibility.Collapsed, ((ProgressBar)window.FindName("BusyIndicator")).Visibility);
            }
            finally { window.Destroy(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopKeepsProgressVisibleButCompletedSessionCanBeDismissed(bool finished)
    {
        await OnSta(() =>
        {
            int stops = 0;
            var window = new TranslationOverlay(new TranslationSettings(), () => stops++, _ => { });
            try
            {
                window.Show();
                if (finished) window.Finish("Błąd źródła audio");
                var button = FindButtons(window).Single(b => b.Content?.ToString()?.Contains("Stop") == true || b.Content?.ToString() == "Zamknij");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(!finished, window.IsVisible);
                Assert.Equal(finished ? 0 : 1, stops);
                if (!finished)
                {
                    Assert.False(button.IsEnabled);
                    Assert.Contains("Kończę", ((TextBlock)window.FindName("StatusLabel")).Text);
                    window.Status("Mikrofon: nasłuchiwanie");
                    Assert.Contains("Kończę", ((TextBlock)window.FindName("StatusLabel")).Text);
                    window.Finish("Zatrzymano");
                    Assert.True(button.IsEnabled);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(window.IsVisible);
                    Assert.Equal(1, stops);
                }
            }
            finally { window.Destroy(); }
        });
    }

    [Fact]
    public async Task ClosingActiveWindowStopsTranslation()
    {
        await OnSta(() =>
        {
            int stops = 0;
            var window = new TranslationOverlay(new TranslationSettings(), () => stops++, _ => { });
            try
            {
                window.Show(); window.Close();
                Assert.True(window.IsVisible);
                Assert.Equal(1, stops);
                window.Close();
                Assert.Equal(1, stops);
                window.Finish("Zatrzymano"); window.Close();
                Assert.False(window.IsVisible);
            }
            finally { window.Destroy(); }
        });
    }

    private static IEnumerable<Button> FindButtons(DependencyObject node)
    {
        if (node is Button button) yield return button;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var found in FindButtons(child)) yield return found;
    }

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject node)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
            yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }

    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
}
