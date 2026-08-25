using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Infrastructure.Recowilk;

public sealed class ProtectedFileRecordingCatalog : IRecordingCatalog
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _directory;
    private readonly IRecordingCatalogProtector _protector;
    private readonly object _gate = new();

    public ProtectedFileRecordingCatalog()
        : this(AppPaths.RecordingCatalogDirectory, new DpapiRecordingCatalogProtector()) { }

    internal ProtectedFileRecordingCatalog(string directory, IRecordingCatalogProtector protector)
    {
        _directory = directory;
        _protector = protector;
    }

    public event EventHandler<RecordingCatalogChangedEventArgs>? Changed;

    public IReadOnlyList<RecordingCatalogEntry> List()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var result = new List<RecordingCatalogEntry>();
            foreach (var path in Directory.EnumerateFiles(_directory, "*.recording"))
            {
                try { result.Add(Load(path)); }
                catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or InvalidDataException or FormatException)
                { Quarantine(path); }
            }
            return result.OrderByDescending(x => x.StartedAt).ToArray();
        }
    }

    public RecordingCatalogEntry? Get(Guid recordingId)
    {
        lock (_gate)
        {
            var path = PathFor(recordingId);
            if (!File.Exists(path)) return null;
            try { return Load(path); }
            catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or InvalidDataException or FormatException)
            { Quarantine(path); return null; }
        }
    }

    public void Upsert(RecordingCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.RecordingId == Guid.Empty || string.IsNullOrWhiteSpace(entry.AudioPath))
            throw new InvalidDataException("Wpis katalogu nagrań nie ma identyfikatora lub ścieżki MP3.");
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            entry.SchemaVersion = SchemaVersion;
            var target = PathFor(entry.RecordingId);
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var bytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions));
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                           4096, FileOptions.WriteThrough))
                { stream.Write(bytes); stream.Flush(true); }
                File.Move(temporary, target, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        Changed?.Invoke(this, new RecordingCatalogChangedEventArgs(entry.RecordingId));
    }

    public void ReconcileRecordingsDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        var known = List().Select(x => Path.GetFullPath(x.AudioPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "*.mp3", SearchOption.TopDirectoryOnly))
        {
            var fullPath = Path.GetFullPath(path);
            if (known.Contains(fullPath)) continue;
            var info = new FileInfo(fullPath);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()));
            Upsert(new RecordingCatalogEntry
            {
                RecordingId = new Guid(hash.AsSpan(0, 16)),
                AudioPath = fullPath,
                AudioSizeBytes = info.Length,
                StartedAt = info.CreationTimeUtc,
                StoppedAt = info.LastWriteTimeUtc,
                Title = Path.GetFileNameWithoutExtension(fullPath),
                ExportStatus = RecordingExportStatus.LocalOnly
            });
            known.Add(fullPath);
        }
    }

    private RecordingCatalogEntry Load(string path)
    {
        var entry = JsonSerializer.Deserialize<RecordingCatalogEntry>(
            _protector.Unprotect(File.ReadAllBytes(path)), JsonOptions)
            ?? throw new InvalidDataException("Pusty wpis katalogu nagrań.");
        if (entry.SchemaVersion != SchemaVersion || entry.RecordingId == Guid.Empty || string.IsNullOrWhiteSpace(entry.AudioPath))
            throw new InvalidDataException("Nieobsługiwana wersja wpisu katalogu nagrań.");
        if (!File.Exists(entry.AudioPath) && entry.ExportStatus != RecordingExportStatus.Exported)
            entry.ExportStatus = RecordingExportStatus.MissingFile;
        return entry;
    }

    private string PathFor(Guid recordingId) => Path.Combine(_directory, $"{recordingId:N}.recording");
    private static void Quarantine(string path)
    {
        if (File.Exists(path)) File.Move(path, path + ".corrupt." + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true);
    }
}

internal interface IRecordingCatalogProtector
{
    byte[] Protect(byte[] value);
    byte[] Unprotect(byte[] value);
}

internal sealed class DpapiRecordingCatalogProtector : IRecordingCatalogProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MeetingAudioRecorder.Recordings.v1");
    public byte[] Protect(byte[] value) => ProtectedData.Protect(value, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] value) => ProtectedData.Unprotect(value, Entropy, DataProtectionScope.CurrentUser);
}
