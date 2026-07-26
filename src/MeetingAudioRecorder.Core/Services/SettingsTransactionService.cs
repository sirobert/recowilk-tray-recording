using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Services;

public readonly record struct SettingsCommitResult(bool Success, string? ErrorMessage)
{
    public static SettingsCommitResult Ok() => new(true, null);
    public static SettingsCommitResult Fail(string message) => new(false, message);
}

public static class SettingsTransactionService
{
    public static SettingsCommitResult TryCommit(
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        AppSettings candidate)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(hotkeyService);
        ArgumentNullException.ThrowIfNull(candidate);

        var current = settingsService.Current;
        var previousHotkey = current.Hotkey.Clone();
        var wasRegistered = hotkeyService.IsRegistered;
        var requiresRegistration = !wasRegistered || !candidate.Hotkey.EqualsHotkey(previousHotkey);

        if (requiresRegistration && !hotkeyService.Register(candidate.Hotkey))
        {
            return SettingsCommitResult.Fail(
                hotkeyService.LastError ?? "Nie udało się zarejestrować nowego skrótu.");
        }

        try
        {
            settingsService.Save(candidate);
            return SettingsCommitResult.Ok();
        }
        catch (Exception ex)
        {
            var rollbackSucceeded = true;
            if (requiresRegistration)
            {
                rollbackSucceeded = wasRegistered
                    ? hotkeyService.Register(previousHotkey)
                    : TryUnregister(hotkeyService);
            }

            var suffix = rollbackSucceeded
                ? " Poprzedni skrót pozostał aktywny."
                : " Nie udało się przywrócić poprzedniego skrótu.";
            return SettingsCommitResult.Fail(ex.Message + suffix);
        }
    }

    private static bool TryUnregister(IHotkeyService hotkeyService)
    {
        try
        {
            hotkeyService.Unregister();
            return !hotkeyService.IsRegistered;
        }
        catch
        {
            return false;
        }
    }
}
