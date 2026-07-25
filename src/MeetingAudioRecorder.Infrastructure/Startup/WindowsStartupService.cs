using MeetingAudioRecorder.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace MeetingAudioRecorder.Infrastructure.Startup;

/// <summary>
/// Autostart przez HKCU\...\Run — bez uprawnień administratora.
/// </summary>
public sealed class WindowsStartupService : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MeetingAudioRecorder";
    private readonly ILogger<WindowsStartupService> _logger;

    public WindowsStartupService(ILogger<WindowsStartupService> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Odczyt autostartu");
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey, true);

            if (enabled)
            {
                var exe = Environment.ProcessPath
                           ?? Path.Combine(AppContext.BaseDirectory, "MeetingAudioRecorder.exe");
                // Cudzysłowy na wypadek spacji w ścieżce
                key.SetValue(ValueName, $"\"{exe}\"");
                _logger.LogInformation("Włączono autostart: {Exe}", exe);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                _logger.LogInformation("Wyłączono autostart");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się ustawić autostartu");
            throw new InvalidOperationException("Nie udało się zmienić ustawienia autostartu Windows.", ex);
        }
    }
}
