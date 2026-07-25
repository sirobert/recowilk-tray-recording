using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;

    bool IsRegistered { get; }
    string? LastError { get; }

    bool Register(HotkeySettings settings);
    void Unregister();
}
