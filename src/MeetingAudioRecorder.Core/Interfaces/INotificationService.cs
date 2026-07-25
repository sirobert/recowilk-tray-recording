namespace MeetingAudioRecorder.Core.Interfaces;

public interface INotificationService
{
    void ShowInfo(string title, string message, string? openPath = null);
    void ShowSuccess(string title, string message, string? openPath = null);
    void ShowWarning(string title, string message);
    void ShowError(string title, string message);
}
