using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IHotkeyService : IDisposable
{
    event EventHandler? HotkeyPressed;

    bool IsRegistered { get; }
    string? LastError { get; }

    /// <summary>
    /// Rejestruje nowy skrót transakcyjnie. Niepowodzenie pozostawia poprzedni skrót aktywny.
    /// </summary>
    bool Register(HotkeySettings settings);
    void Unregister();
}
