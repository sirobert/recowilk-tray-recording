using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Hardcodet.Wpf.TaskbarNotification;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.App.Services;

public sealed class TrayIconController : ObservableObject, IDisposable
{
    private readonly TaskbarIcon tray;
    private readonly TextBlock tooltip = new() { Margin = new Thickness(10), MaxWidth = 460, TextWrapping = TextWrapping.Wrap };
    private RecorderTrayStatus status = new();
    private bool disposed;
    private (bool Recording, bool Translation, bool Attention)? iconState;
    public string RecordingText => status.RecordingText;
    public string TranslationText => status.TranslationText;
    public string UploadText => status.UploadText;

    public TrayIconController(TaskbarIcon tray)
    {
        this.tray = tray;
        tray.TrayToolTip = new Border { Background = SystemColors.InfoBrush, Child = tooltip };
        tooltip.Foreground = SystemColors.InfoTextBrush;
        Apply();
    }

    public void Update(AppRecordingState state, TimeSpan? duration = null, string? error = null) =>
        Dispatch(() => status = status with { Recording = state, Duration = duration ?? TimeSpan.Zero, RecordingError = error });
    public void UpdateTranslation(TranslationActivity translation) => Dispatch(() => status = status with { Translation = translation });
    public void UpdateUploads(UploadActivity upload) => Dispatch(() => status = status with { Upload = upload });
    private void Dispatch(Action update)
    {
        var dispatcher = tray.Dispatcher;
        if (dispatcher.HasShutdownStarted) return;
        void Change() { if (disposed) return; update(); Apply(); }
        if (dispatcher.CheckAccess()) Change(); else _ = dispatcher.InvokeAsync(Change);
    }
    public void Dispose() => disposed = true;
    private void Apply()
    {
        var markers = (status.RecordingMarker, status.TranslationMarker, status.NeedsAttention);
        if (iconState != markers) { tray.IconSource = CreateIcon(status); iconState = markers; }
        tooltip.Text = status.Tooltip;
        tray.ToolTipText = "Recorder — najedź, aby zobaczyć stany funkcji";
        OnPropertyChanged(nameof(RecordingText)); OnPropertyChanged(nameof(TranslationText)); OnPropertyChanged(nameof(UploadText));
    }

    public static ImageSource CreateIcon(AppRecordingState state) => CreateIcon(new RecorderTrayStatus { Recording = state });
    public static ImageSource CreateIcon(RecorderTrayStatus status)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(60, 120, 200)), null, new Rect(1, 1, 30, 30), 6, 6);
            // Permanent recorder glyph, with independent badges in separate corners.
            drawing.DrawRoundedRectangle(Brushes.White, null, new Rect(9, 7, 6, 11), 3, 3);
            drawing.DrawLine(new Pen(Brushes.White, 2), new Point(7, 20), new Point(17, 20));
            drawing.DrawLine(new Pen(Brushes.White, 2), new Point(12, 18), new Point(12, 24));
            if (status.RecordingMarker)
                drawing.DrawEllipse(Brushes.Red, new Pen(Brushes.White, 1), new Point(7, 7), 5, 5);
            if (status.TranslationMarker)
            {
                drawing.DrawRoundedRectangle(Brushes.Turquoise, new Pen(Brushes.White, 1), new Rect(18, 17, 13, 14), 2, 2);
                drawing.DrawLine(new Pen(Brushes.Black, 2), new Point(21, 21), new Point(28, 21));
                drawing.DrawLine(new Pen(Brushes.Black, 2), new Point(24.5, 21), new Point(24.5, 28));
            }
            if (status.NeedsAttention)
            {
                drawing.DrawEllipse(Brushes.Gold, new Pen(Brushes.White, 1), new Point(25, 7), 6, 6);
                drawing.DrawLine(new Pen(Brushes.Black, 2), new Point(25, 3), new Point(25, 7));
                drawing.DrawEllipse(Brushes.Black, null, new Point(25, 10), 1, 1);
            }
        }
        var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
