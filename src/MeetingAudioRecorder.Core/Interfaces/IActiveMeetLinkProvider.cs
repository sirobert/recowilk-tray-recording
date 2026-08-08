using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IActiveMeetLinkProvider
{
    event EventHandler? ActiveLinksChanged;

    Task<IReadOnlyList<BrowserMeetLink>> GetActiveLinksAsync(
        CancellationToken cancellationToken = default);
}
