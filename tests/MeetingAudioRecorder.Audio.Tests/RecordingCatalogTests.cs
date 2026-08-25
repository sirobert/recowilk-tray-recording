using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Infrastructure.Recowilk;

namespace MeetingAudioRecorder.Audio.Tests;

public sealed class RecordingCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mar-catalog-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Catalog_roundtrip_is_encrypted_and_preserves_metadata()
    {
        Directory.CreateDirectory(_directory);
        var catalog = new ProtectedFileRecordingCatalog(_directory, new TestProtector());
        var entry = Entry();

        catalog.Upsert(entry);

        var file = Assert.Single(Directory.EnumerateFiles(_directory, "*.recording"));
        var persisted = File.ReadAllText(file);
        Assert.DoesNotContain("alice@example.com", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Poufny opis", persisted, StringComparison.OrdinalIgnoreCase);
        var loaded = Assert.Single(new ProtectedFileRecordingCatalog(_directory, new TestProtector()).List());
        Assert.Equal(entry.RecordingId, loaded.RecordingId);
        Assert.Equal("Planowanie", loaded.Title);
        Assert.Equal("alice@example.com", Assert.Single(loaded.Participants).Email);
    }

    [Fact]
    public void Reconcile_imports_untracked_mp3_without_inventing_private_metadata()
    {
        Directory.CreateDirectory(_directory);
        var audio = Path.Combine(_directory, "Nagranie_2026-08-25_12-30-00.mp3");
        File.WriteAllBytes(audio, new byte[512]);
        var catalogDirectory = Path.Combine(_directory, "catalog");
        var catalog = new ProtectedFileRecordingCatalog(catalogDirectory, new TestProtector());

        catalog.ReconcileRecordingsDirectory(_directory);

        var imported = Assert.Single(catalog.List());
        Assert.Equal(audio, imported.AudioPath);
        Assert.Equal(RecordingExportStatus.LocalOnly, imported.ExportStatus);
        Assert.Empty(imported.Participants);
        Assert.Null(imported.Description);
    }

    [Fact]
    public void Corrupt_entry_is_quarantined_and_does_not_hide_valid_entries()
    {
        Directory.CreateDirectory(_directory);
        var catalog = new ProtectedFileRecordingCatalog(_directory, new TestProtector());
        catalog.Upsert(Entry());
        File.WriteAllText(Path.Combine(_directory, "broken.recording"), "not-protected-data");

        var entries = catalog.List();

        Assert.Single(entries);
        Assert.Single(Directory.EnumerateFiles(_directory, "broken.recording.corrupt.*"));
    }

    [Fact]
    public void Production_dpapi_protector_roundtrips_without_plaintext()
    {
        var protector = new DpapiRecordingCatalogProtector();
        var plain = System.Text.Encoding.UTF8.GetBytes("alice@example.com|Poufny opis");

        var encrypted = protector.Protect(plain);

        Assert.NotEqual(plain, encrypted);
        Assert.Equal(plain, protector.Unprotect(encrypted));
        Assert.DoesNotContain("alice@example.com", System.Text.Encoding.UTF8.GetString(encrypted),
            StringComparison.OrdinalIgnoreCase);
    }

    private static RecordingCatalogEntry Entry() => new()
    {
        RecordingId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        AudioPath = @"C:\recordings\meeting.mp3",
        AudioSizeBytes = 1234,
        StartedAt = DateTimeOffset.Parse("2026-08-25T10:01:00Z"),
        StoppedAt = DateTimeOffset.Parse("2026-08-25T10:31:00Z"),
        DurationMs = 1_800_000,
        Title = "Planowanie",
        Description = "Poufny opis",
        Provider = "GoogleMeet",
        MeetingUrl = "https://meet.google.com/abc-defg-hij",
        Participants = [new RecordingCatalogParticipant("Alice", "alice@example.com", "Organizer")],
        ExportStatus = RecordingExportStatus.RetryScheduled,
        HttpStatusCode = 500,
        TraceId = "trace-test"
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class TestProtector : IRecordingCatalogProtector
    {
        public byte[] Protect(byte[] value) => System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(value));
        public byte[] Unprotect(byte[] value) => Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(value));
    }
}
