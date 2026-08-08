namespace MeetingAudioRecorder.Core.Models;

/// <summary>
/// Ścieżki aplikacji w profilu użytkownika.
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "MeetingAudioRecorder";

    public static string LocalAppData =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string SettingsPath => Path.Combine(LocalAppData, "settings.json");
    public static string GoogleTokenPath => Path.Combine(LocalAppData, "google-token.dat");
    public static string LogsDirectory => Path.Combine(LocalAppData, "Logs");
    public static string TempDirectory => Path.Combine(LocalAppData, "Temp");
    public static string BrowserDirectory => Path.Combine(LocalAppData, "Browser");
    public static string BrowserStatePath => Path.Combine(BrowserDirectory, "active-meet.json");
    public static string BrowserExtensionDirectory => Path.Combine(BrowserDirectory, "MeetingOrgniazerGemini");
    public static string MutexName => @"Local\MeetingAudioRecorder_SingleInstance_v1";

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(LocalAppData);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(TempDirectory);
    }
}
