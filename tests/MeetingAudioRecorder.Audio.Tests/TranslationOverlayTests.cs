using System.Windows;
using System.Windows.Controls;
using MeetingAudioRecorder.App.Views;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Audio.Tests;

public class TranslationOverlayTests
{
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
