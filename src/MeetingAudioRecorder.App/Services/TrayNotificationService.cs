using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using MeetingAudioRecorder.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.App.Services;

public sealed class TrayNotificationService : INotificationService
{
    private readonly ILogger<TrayNotificationService> _logger;
    private TaskbarIcon? _tray;
    private string? _pendingOpenPath;

    public TrayNotificationService(ILogger<TrayNotificationService> logger)
    {
        _logger = logger;
    }

    public void Attach(TaskbarIcon tray)
    {
        _tray = tray;
        _tray.TrayBalloonTipClicked += OnBalloonClicked;
    }

    public void ShowInfo(string title, string message, string? openPath = null)
        => Show(title, message, BalloonIcon.Info, openPath);

    public void ShowSuccess(string title, string message, string? openPath = null)
        => Show(title, message, BalloonIcon.Info, openPath);

    public void ShowWarning(string title, string message)
        => Show(title, message, BalloonIcon.Warning, null);

    public void ShowError(string title, string message)
        => Show(title, message, BalloonIcon.Error, null);

    private void Show(string title, string message, BalloonIcon icon, string? openPath)
    {
        _pendingOpenPath = openPath;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        void Apply()
        {
            try
            {
                _tray?.ShowBalloonTip(title, message, icon);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Powiadomienie: {Title} {Message}", title, message);
            }
        }

        if (dispatcher.CheckAccess())
            Apply();
        else
            _ = dispatcher.InvokeAsync(Apply);
    }

    private void OnBalloonClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_pendingOpenPath))
            return;

        try
        {
            var path = _pendingOpenPath;
            if (File.Exists(path))
                path = Path.GetDirectoryName(path) ?? path;

            if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Otwarcie folderu z powiadomienia");
        }
    }
}
