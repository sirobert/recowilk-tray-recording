namespace MeetingAudioRecorder.Audio.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsIntegrationFactAttribute : FactAttribute
{
    public WindowsIntegrationFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Test wymaga systemu Windows.";
            return;
        }

        if (!string.Equals(
                Environment.GetEnvironmentVariable("MAR_RUN_WINDOWS_INTEGRATION"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Ustaw MAR_RUN_WINDOWS_INTEGRATION=1, aby uruchomić testy Media Foundation.";
        }
    }
}
