namespace MeetingAudioRecorder.Core.Interfaces;

public interface IStartupService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}
