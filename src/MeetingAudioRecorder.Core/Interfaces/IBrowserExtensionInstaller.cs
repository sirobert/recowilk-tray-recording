using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IBrowserExtensionInstaller
{
    Task<BrowserExtensionPreparationResult> PrepareAsync(
        SupportedBrowser browser,
        CancellationToken cancellationToken = default);
}
