using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.App.Services;

/// <summary>
/// Aktualizuje ikonę tray i tooltip w zależności od stanu.
/// </summary>
public sealed class TrayIconController
{
    private readonly TaskbarIcon _tray;

    public TrayIconController(TaskbarIcon tray)
    {
        _tray = tray;
    }

    public void Update(AppRecordingState state, TimeSpan? duration = null, string? error = null)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _tray.IconSource = CreateIcon(state);
            _tray.ToolTipText = BuildTooltip(state, duration, error);
        });
    }

    private static string BuildTooltip(AppRecordingState state, TimeSpan? duration, string? error) => state switch
    {
        AppRecordingState.Idle => "Meeting Audio Recorder — gotowy",
        AppRecordingState.Starting => "Meeting Audio Recorder — uruchamianie…",
        AppRecordingState.Recording => $"Nagrywanie… {FormatDuration(duration ?? TimeSpan.Zero)}",
        AppRecordingState.Stopping => "Zatrzymywanie…",
        AppRecordingState.Processing => "Przetwarzanie pliku…",
        AppRecordingState.Completed => "Nagranie zapisane",
        AppRecordingState.Error => "Błąd: " + (error ?? "szczegóły w logu"),
        _ => "Meeting Audio Recorder"
    };

    private static string FormatDuration(TimeSpan t)
        => t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss")
            : t.ToString(@"mm\:ss");

    /// <summary>
    /// Generuje prostą ikonę bitmapową w pamięci (bez zewnętrznych plików).
    /// </summary>
    public static ImageSource CreateIcon(AppRecordingState state)
    {
        var color = state switch
        {
            AppRecordingState.Recording => Color.FromRgb(220, 50, 50),
            AppRecordingState.Processing or AppRecordingState.Starting or AppRecordingState.Stopping
                => Color.FromRgb(230, 160, 20),
            AppRecordingState.Error => Color.FromRgb(180, 40, 180),
            AppRecordingState.Completed => Color.FromRgb(40, 160, 70),
            _ => Color.FromRgb(60, 120, 200)
        };

        const int size = 32;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - size / 2.0 + 0.5;
                var dy = y - size / 2.0 + 0.5;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                var idx = (y * size + x) * 4;

                if (dist <= 13)
                {
                    // Koło tła
                    pixels[idx] = color.B;
                    pixels[idx + 1] = color.G;
                    pixels[idx + 2] = color.R;
                    pixels[idx + 3] = 255;

                    // Wewnętrzny indicator: kropka / kwadrat
                    if (state == AppRecordingState.Recording && dist <= 6)
                    {
                        // Czerwona kropka (rec)
                        pixels[idx] = 40;
                        pixels[idx + 1] = 40;
                        pixels[idx + 2] = 40;
                    }
                    else if (state is AppRecordingState.Processing or AppRecordingState.Starting
                             && Math.Abs(dx) < 5 && Math.Abs(dy) < 5)
                    {
                        pixels[idx] = 30;
                        pixels[idx + 1] = 30;
                        pixels[idx + 2] = 30;
                    }
                }
                else
                {
                    pixels[idx + 3] = 0; // transparent
                }
            }
        }

        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bmp.Freeze();
        return bmp;
    }
}
