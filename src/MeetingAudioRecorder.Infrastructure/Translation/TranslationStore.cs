using System.Security.Cryptography;
using System.Text.Json;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Infrastructure.Translation;

// Separate file types and location: never registered with the MP3 catalog or upload queue.
public sealed class TranslationStore
{
    private readonly string root;
    private readonly object gate = new();
    public TranslationStore(string? directory = null) => root = directory ?? Path.Combine(AppPaths.LocalAppData, "Translations");
    private string Folder(Guid id) => Path.Combine(root, id.ToString("N"));
    public void Create(TranslationSession session)
    {
        lock (gate) { Directory.CreateDirectory(Folder(session.Id)); Write(Path.Combine(Folder(session.Id), "session.dat"), session); }
    }
    public void Append(Guid id, TranslationCaption caption)
    {
        if (caption.IsDraft) return;
        if (caption.Source is not ("output" or "microphone") || caption.Id < 1 || !double.IsFinite(caption.StartMs) || !double.IsFinite(caption.EndMs) || caption.StartMs < 0 || caption.EndMs < caption.StartMs)
            throw new InvalidDataException("Nieprawidłowa fraza tłumaczenia.");
        lock (gate)
        {
            var target = Path.Combine(Folder(id), $"{caption.Source}-{caption.Id:D9}.segment");
            if (!File.Exists(target)) Write(target, caption);
        }
    }
    public void Complete(Guid id, string state)
    {
        lock (gate) { var path = Path.Combine(Folder(id), "session.dat"); var session = Read<TranslationSession>(path); session.State = state; session.StoppedAt = DateTimeOffset.UtcNow; Write(path, session); }
    }
    public IReadOnlyList<TranslationSession> List()
    {
        lock (gate)
        {
            if (!Directory.Exists(root)) return [];
            var result = new List<TranslationSession>();
            foreach (var path in Directory.EnumerateFiles(root, "session.dat", SearchOption.AllDirectories))
                try { result.Add(Read<TranslationSession>(path)); }
                catch (Exception e) when (e is IOException or CryptographicException or JsonException or InvalidDataException) { /* Preserve damaged files for inspection. */ }
            return result.OrderByDescending(s => s.StartedAt).ToArray();
        }
    }
    public TranslationDocument Load(Guid id)
    {
        lock (gate)
        {
            var session = Read<TranslationSession>(Path.Combine(Folder(id), "session.dat"));
            var captions = new List<TranslationCaption>(); int bad = 0;
            foreach (var path in Directory.EnumerateFiles(Folder(id), "*.segment"))
                try { captions.Add(Read<TranslationCaption>(path)); }
                catch (Exception e) when (e is IOException or CryptographicException or JsonException or InvalidDataException) { bad++; }
            return new(session, captions.OrderBy(c => c.StartMs).ThenBy(c => c.Source).ThenBy(c => c.Id).ToArray(), bad);
        }
    }
    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) ?? throw new InvalidDataException("Nieczytelny zapis tłumaczenia.");
    private static void Write<T>(string path, T value)
    {
        var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value), null, DataProtectionScope.CurrentUser);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { stream.Write(bytes); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
