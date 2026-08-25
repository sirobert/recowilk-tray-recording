using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Services;

public static class RecowilkSettingsTransactionService
{
    public static async Task<SettingsCommitResult> TryCommitAsync(
        ISettingsService settingsService,
        IHotkeyService hotkeyService,
        IRecowilkCredentialStore credentialStore,
        IRecowilkUploadQueue uploadQueue,
        AppSettings candidate,
        string? candidateKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentNullException.ThrowIfNull(hotkeyService);
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentNullException.ThrowIfNull(uploadQueue);
        ArgumentNullException.ThrowIfNull(candidate);

        var validation = settingsService.Validate(candidate);
        if (!validation.IsValid)
            return SettingsCommitResult.Fail(string.Join(" ", validation.Errors));

        var normalizedKey = string.IsNullOrWhiteSpace(candidateKey) ? null : candidateKey.Trim();
        if (candidate.RecowilkUploadEnabled && normalizedKey is null && !credentialStore.HasKey)
            return SettingsCommitResult.Fail("Wklej klucz API RecoWilk.");

        if (normalizedKey is not null)
        {
            var connection = await uploadQueue.TestConnectionAsync(candidate.RecowilkBaseUrl, normalizedKey, cancellationToken)
                .ConfigureAwait(false);
            if (!connection.Success)
                return SettingsCommitResult.Fail("Nowy klucz RecoWilk nie przeszedł weryfikacji.");
        }

        var previousKey = credentialStore.Load();
        var changedSecret = false;
        try
        {
            if (normalizedKey is not null)
            {
                credentialStore.Save(normalizedKey);
                changedSecret = true;
            }

            var result = SettingsTransactionService.TryCommit(settingsService, hotkeyService, candidate);
            if (!result.Success && changedSecret)
                RestoreSecret(credentialStore, previousKey);
            return result;
        }
        catch (Exception ex)
        {
            if (changedSecret)
            {
                try { RestoreSecret(credentialStore, previousKey); }
                catch { return SettingsCommitResult.Fail(ex.Message + " Nie udało się przywrócić poprzedniego klucza RecoWilk."); }
            }
            return SettingsCommitResult.Fail(ex.Message);
        }
    }

    private static void RestoreSecret(IRecowilkCredentialStore store, string? previousKey)
    {
        if (previousKey is null) store.Clear();
        else store.Save(previousKey);
    }
}
