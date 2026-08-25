using System.Security.Cryptography;
using System.Text;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Infrastructure.Recowilk;

public sealed class ProtectedFileRecowilkCredentialStore : IRecowilkCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MeetingAudioRecorder.Recowilk.v1");
    public bool HasKey => File.Exists(AppPaths.RecowilkCredentialPath);

    public string? Load()
    {
        if (!HasKey) return null;
        try
        {
            var protectedBytes = File.ReadAllBytes(AppPaths.RecowilkCredentialPath);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                protectedBytes, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException) { return null; }
    }

    public void Save(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Klucz API jest pusty.", nameof(value));
        AppPaths.EnsureDirectories();
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), Entropy, DataProtectionScope.CurrentUser);
        var temporary = AppPaths.RecowilkCredentialPath + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, AppPaths.RecowilkCredentialPath, true);
    }

    public void Clear()
    {
        if (File.Exists(AppPaths.RecowilkCredentialPath)) File.Delete(AppPaths.RecowilkCredentialPath);
    }
}
