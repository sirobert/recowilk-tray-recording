using System.Windows;
using System.Windows.Controls;
using MeetingAudioRecorder.App.Views;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Audio.Tests;

public class TranslationOverlayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopButtonDismissesWindowEvenAfterSourceFailure(bool finished)
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
                Assert.False(window.IsVisible);
                Assert.Equal(finished ? 0 : 1, stops);
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
                Assert.False(window.IsVisible);
                Assert.Equal(1, stops);
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
