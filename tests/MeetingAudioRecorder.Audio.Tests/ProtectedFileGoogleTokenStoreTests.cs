using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Infrastructure.Google;

namespace MeetingAudioRecorder.Audio.Tests;

public sealed class ProtectedFileGoogleTokenStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "mar-google-token-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoad_RoundTripsTokenWithoutPlaintextOnDisk()
    {
        var path = Path.Combine(_directory, "google-token.dat");
        var store = new ProtectedFileGoogleTokenStore(path);
        var token = CreateToken();

        await store.SaveAsync(token);
        var loaded = await store.LoadAsync();
        var persistedBytes = await File.ReadAllBytesAsync(path);
        var persistedText = System.Text.Encoding.UTF8.GetString(persistedBytes);

        Assert.NotNull(loaded);
        Assert.Equal(token.AccessToken, loaded.AccessToken);
        Assert.Equal(token.RefreshToken, loaded.RefreshToken);
        Assert.Equal(token.ExpiresAtUtc, loaded.ExpiresAtUtc);
        Assert.Equal(token.AccountEmail, loaded.AccountEmail);
        Assert.Equal(token.AccountUserId, loaded.AccountUserId);
        Assert.Equal(token.GrantedScopes, loaded.GrantedScopes);
        Assert.DoesNotContain(token.AccessToken, persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain(token.RefreshToken, persistedText, StringComparison.Ordinal);
        Assert.DoesNotContain(token.AccountEmail, persistedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_RemovesPersistedToken()
    {
        var path = Path.Combine(_directory, "google-token.dat");
        var store = new ProtectedFileGoogleTokenStore(path);
        await store.SaveAsync(CreateToken());

        await store.DeleteAsync();

        Assert.False(File.Exists(path));
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task Load_CorruptPayloadThrowsWithoutDeletingEvidence()
    {
        var path = Path.Combine(_directory, "google-token.dat");
        Directory.CreateDirectory(_directory);
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var store = new ProtectedFileGoogleTokenStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());

        Assert.True(File.Exists(path));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Sprzątanie best effort; wynik testu nie zależy od usuwania katalogu.
        }
    }

    private static GoogleOAuthToken CreateToken()
        => new()
        {
            AccessToken = "access-token-that-must-not-be-plain-text",
            RefreshToken = "refresh-token-that-must-not-be-plain-text",
            ExpiresAtUtc = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero),
            AccountEmail = "recorder@example.com",
            AccountUserId = "users/1234567890",
            GrantedScopes =
            [
                "openid",
                "https://www.googleapis.com/auth/calendar.events.readonly",
                "https://www.googleapis.com/auth/meetings.space.readonly"
            ]
        };
}
