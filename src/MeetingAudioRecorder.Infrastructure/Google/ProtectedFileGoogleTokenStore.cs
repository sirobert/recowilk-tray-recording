using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Infrastructure.Google;

public sealed class ProtectedFileGoogleTokenStore : IGoogleTokenStore
{
    private const int CurrentVersion = 2;
    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("MeetingAudioRecorder.GoogleOAuthToken.v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProtectedFileGoogleTokenStore()
        : this(AppPaths.GoogleTokenPath)
    {
    }

    public ProtectedFileGoogleTokenStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<GoogleOAuthToken?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
                return null;

            try
            {
                var protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
                var jsonBytes = ProtectedData.Unprotect(
                    protectedBytes,
                    OptionalEntropy,
                    DataProtectionScope.CurrentUser);
                var envelope = JsonSerializer.Deserialize<TokenEnvelope>(jsonBytes, JsonOptions);
                if (envelope is null || envelope.Version != CurrentVersion || envelope.Token is null)
                    throw new InvalidDataException("Nieobsługiwana lub niepełna wersja magazynu tokenu Google.");

                Validate(envelope.Token);
                return envelope.Token;
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or NotSupportedException)
            {
                throw new InvalidDataException(
                    "Nie można odszyfrować magazynu tokenu Google dla bieżącego użytkownika Windows.",
                    ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(GoogleOAuthToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        Validate(token);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path)
                            ?? throw new InvalidOperationException("Brak katalogu magazynu tokenu Google.");
            Directory.CreateDirectory(directory);

            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(
                new TokenEnvelope(CurrentVersion, token),
                JsonOptions);
            var protectedBytes = ProtectedData.Protect(
                jsonBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);

            temporaryPath = Path.Combine(
                directory,
                Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Plik jest zaszyfrowany; sprzątanie zostanie ponowione przy następnym zapisie.
                }
            }

            _gate.Release();
        }
    }

    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            File.Delete(_path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void Validate(GoogleOAuthToken token)
    {
        if (string.IsNullOrWhiteSpace(token.AccessToken)
            || string.IsNullOrWhiteSpace(token.RefreshToken)
            || string.IsNullOrWhiteSpace(token.AccountEmail)
            || string.IsNullOrWhiteSpace(token.AccountUserId)
            || string.IsNullOrWhiteSpace(token.ClientId)
            || string.IsNullOrWhiteSpace(token.TokenEndpoint))
        {
            throw new InvalidDataException("Token Google nie zawiera wymaganych danych.");
        }
    }

    private sealed record TokenEnvelope(int Version, GoogleOAuthToken Token);
}
