using System.Diagnostics;
using System.Text.RegularExpressions;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.Infrastructure.Recovery;

public sealed class RecordingRecoveryService : IRecordingRecoveryService
{
    private static readonly Regex TempPattern = new(
        @"^(?<id>[0-9a-fA-F]{32})_(?<kind>microphone|loopback)\.tmp\.wav$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ILogger<RecordingRecoveryService> _logger;

    public RecordingRecoveryService(ILogger<RecordingRecoveryService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<RecoverableRecording> FindRecoverableRecordings()
    {
        AppPaths.EnsureDirectories();
        var dir = AppPaths.TempDirectory;
        if (!Directory.Exists(dir))
            return Array.Empty<RecoverableRecording>();

        var map = new Dictionary<Guid, (string? mic, string? loop, long micSize, long loopSize)>();

        foreach (var file in Directory.EnumerateFiles(dir, "*.tmp.wav"))
        {
            var name = Path.GetFileName(file);
            var m = TempPattern.Match(name);
            if (!m.Success) continue;

            if (!Guid.TryParseExact(m.Groups["id"].Value, "N", out var id))
                continue;

            map.TryGetValue(id, out var entry);
            var info = new FileInfo(file);
            if (m.Groups["kind"].Value.Equals("microphone", StringComparison.OrdinalIgnoreCase))
                entry = (file, entry.loop, info.Length, entry.loopSize);
            else
                entry = (entry.mic, file, entry.micSize, info.Length);

            map[id] = entry;
        }

        var result = new List<RecoverableRecording>();
        foreach (var (id, entry) in map)
        {
            var hasMic = entry.mic is not null && entry.micSize > 44;
            var hasLoop = entry.loop is not null && entry.loopSize > 44;
            if (!hasMic && !hasLoop)
                continue;

            result.Add(new RecoverableRecording
            {
                RecordingId = id,
                MicrophoneTempPath = entry.mic ?? string.Empty,
                LoopbackTempPath = entry.loop ?? string.Empty,
                MicrophoneFileSize = entry.micSize,
                LoopbackFileSize = entry.loopSize,
                HasValidMicrophoneFile = hasMic,
                HasValidLoopbackFile = hasLoop,
                DetectedAt = DateTimeOffset.Now
            });
        }

        _logger.LogInformation("Znaleziono {Count} nagrania do odzyskania", result.Count);
        return result;
    }

    public void DeleteRecoverable(RecoverableRecording recoverable)
    {
        TryDelete(recoverable.MicrophoneTempPath);
        TryDelete(recoverable.LoopbackTempPath);
        var mixed = Path.Combine(AppPaths.TempDirectory, $"{recoverable.RecordingId:N}_mixed.tmp.wav");
        TryDelete(mixed);
        _logger.LogInformation("Usunięto pliki odzyskiwania {Id}", recoverable.RecordingId);
    }

    public void OpenTempFolder()
    {
        AppPaths.EnsureDirectories();
        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.TempDirectory,
            UseShellExecute = true
        });
    }

    private void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try { File.Delete(path); }
        catch (Exception ex) { _logger.LogWarning(ex, "Nie usunięto {Path}", path); }
    }
}
