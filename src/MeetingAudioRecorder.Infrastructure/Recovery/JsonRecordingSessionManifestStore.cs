using System.Text.Json;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.Infrastructure.Recovery;

public sealed class JsonRecordingSessionManifestStore : IRecordingSessionManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ILogger<JsonRecordingSessionManifestStore> _logger;
    private readonly string? _directoryOverride;

    public JsonRecordingSessionManifestStore(
        ILogger<JsonRecordingSessionManifestStore> logger,
        string? directoryOverride = null)
    {
        _logger = logger;
        _directoryOverride = directoryOverride;
    }

    public void Save(RecordingSessionManifest manifest)
    {
        var path = GetPath(manifest.RecordingId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = path + ".partial." + Guid.NewGuid().ToString("N");

        try
        {
            var json = JsonSerializer.Serialize(manifest, JsonOptions);
            using (var stream = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(partial, path, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    public RecordingSessionManifest? TryLoad(Guid recordingId)
    {
        var path = GetPath(recordingId);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var manifest = JsonSerializer.Deserialize<RecordingSessionManifest>(json, JsonOptions);
            return manifest is { Version: 1 or 2 } && manifest.RecordingId == recordingId ? manifest : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie można odczytać manifestu sesji {Id}", recordingId);
            return null;
        }
    }

    public void Delete(Guid recordingId) => TryDelete(GetPath(recordingId));

    private string GetPath(Guid recordingId)
    {
        var directory = _directoryOverride ?? AppPaths.TempDirectory;
        return Path.Combine(directory, $"{recordingId:N}.session.json");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A stale manifest/partial remains recoverable on the next start.
        }
    }
}

