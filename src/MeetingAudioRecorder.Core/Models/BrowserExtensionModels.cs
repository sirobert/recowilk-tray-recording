namespace MeetingAudioRecorder.Core.Models;

public enum SupportedBrowser
{
    Chrome = 0,
    Edge = 1
}

public sealed record BrowserExtensionPreparationResult(
    string ExtensionDirectory,
    string ExtensionId,
    bool BrowserOpened);
