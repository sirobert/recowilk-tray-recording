using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Hardcodet.Wpf.TaskbarNotification;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.App.Services;

// Synthetic UI checks only; no capture, credentials, catalog or network access.
internal static class TrayDiagnostics
{
    public static async Task RunAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var both = new RecorderTrayStatus
        {
            Recording = AppRecordingState.Recording,
            Duration = TimeSpan.FromSeconds(754),
            Translation = new(TranslationActivityState.Running, "wyjście + mikrofon"),
            Upload = new("oczekuje: 1")
        };
        RecorderTrayStatus[] samples = [new(), both with { Translation = new() }, both with { Recording = AppRecordingState.Idle },
            both, both with { Upload = new("wymaga uwagi: 1", true) }, both with { Translation = new(TranslationActivityState.Error, Detail: "Mikrofon odłączony") }];
        var panel = new StackPanel { Margin = new Thickness(12) };
        foreach (var sample in samples)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            row.Children.Add(new Image { Source = TrayIconController.CreateIcon(sample), Width = 16, Height = 16, Margin = new Thickness(0, 0, 10, 0) });
            row.Children.Add(new Image { Source = TrayIconController.CreateIcon(sample), Width = 32, Height = 32, Margin = new Thickness(0, 0, 10, 0) });
            row.Children.Add(new TextBlock { Text = sample.Tooltip }); panel.Children.Add(row);
        }
        var window = new Window { Title = "Diagnostyka statusów", Content = panel, SizeToContent = SizeToContent.WidthAndHeight, Background = Brushes.White };
        window.Show(); await Render(window, Path.Combine(directory, "states.png")); window.Close();

        using var tray = (TaskbarIcon)Application.Current.FindResource("TrayIcon");
        tray.Visibility = Visibility.Collapsed;
        using var icons = new TrayIconController(tray);
        var noop = new RelayCommand(() => { });
        tray.DataContext = new
        {
            Activity = icons,
            CanStart = false,
            CanStop = true,
            StartRecordingCommand = noop,
            StopRecordingCommand = noop,
            OpenRecordingsCommand = noop,
            OpenRecordingsFolderCommand = noop,
            ShowLastRecordingCommand = noop,
            OpenSettingsCommand = noop,
            ExitCommand = noop,
            Translation = new { MenuText = "Zatrzymaj tłumaczenie", ToggleCommand = noop, ShowCommand = noop, OpenHistoryCommand = noop, OpenSettingsCommand = noop }
        };
        icons.UpdateTranslation(both.Translation); icons.UpdateUploads(both.Upload);
        await Task.Run(() => icons.Update(AppRecordingState.Recording, both.Duration));
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (!icons.TranslationText.Contains("wyjście + mikrofon") || !icons.RecordingText.Contains("12:34")) throw new InvalidOperationException("Lost concurrent activity");
        var menu = tray.ContextMenu;
        menu.DataContext = tray.DataContext; menu.Placement = PlacementMode.AbsolutePoint; menu.HorizontalOffset = 80; menu.VerticalOffset = 80;
        menu.IsOpen = true; await Render(menu, Path.Combine(directory, "menu.png")); menu.IsOpen = false;
        if (menu.Items.OfType<MenuItem>().Count(item => item.Header is string header && (header.StartsWith("Nagrywanie:") || header.StartsWith("Tłumaczenie:") || header.StartsWith("Wysyłka:"))) != 3)
            throw new InvalidOperationException("Missing status bindings");
    }

    private static async Task Render(FrameworkElement element, string path)
    {
        await Task.Delay(250); element.UpdateLayout();
        var image = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
