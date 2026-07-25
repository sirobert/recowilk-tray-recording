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
    private readonly IWavFileRepairService _wavRepair;
    private readonly IRecordingSessionManifestStore _manifestStore;

    public RecordingRecoveryService(
        ILogger<RecordingRecoveryService> logger,
        IWavFileRepairService wavRepair,
        IRecordingSessionManifestStore manifestStore)
    {
        _logger = logger;
        _wavRepair = wavRepair;
        _manifestStore = manifestStore;
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
            var hasMic = entry.mic is not null && _wavRepair.CanRecover(entry.mic);
            var hasLoop = entry.loop is not null && _wavRepair.CanRecover(entry.loop);
            if (!hasMic && !hasLoop)
                continue;

            var manifest = _manifestStore.TryLoad(id);
            result.Add(new RecoverableRecording
            {
                RecordingId = id,
                MicrophoneTempPath = entry.mic ?? string.Empty,
                LoopbackTempPath = entry.loop ?? string.Empty,
                MicrophoneFileSize = entry.micSize,
                LoopbackFileSize = entry.loopSize,
                HasValidMicrophoneFile = hasMic,
                HasValidLoopbackFile = hasLoop,
                DetectedAt = manifest?.StartedAt ?? GetOldestWriteTime(entry.mic, entry.loop)
            });
        }

        _logger.LogInformation("Znaleziono {Count} nagrania do odzyskania", result.Count);
        return result;
    }

    public RecoverableRecording PrepareForRecovery(RecoverableRecording recoverable)
    {
        string? repairedMic = null;
        string? repairedLoop = null;

        if (recoverable.HasValidMicrophoneFile)
        {
            repairedMic = Path.Combine(
                AppPaths.TempDirectory,
                $"{recoverable.RecordingId:N}_microphone.recovered.wav");
            _wavRepair.RepairToCopy(recoverable.MicrophoneTempPath, repairedMic);
        }

        if (recoverable.HasValidLoopbackFile)
        {
            repairedLoop = Path.Combine(
                AppPaths.TempDirectory,
                $"{recoverable.RecordingId:N}_loopback.recovered.wav");
            _wavRepair.RepairToCopy(recoverable.LoopbackTempPath, repairedLoop);
        }

        if (repairedMic is null && repairedLoop is null)
            throw new InvalidDataException("Żadna ścieżka WAV nie nadaje się do odzyskania.");

        return new RecoverableRecording
        {
            RecordingId = recoverable.RecordingId,
            MicrophoneTempPath = repairedMic ?? string.Empty,
            LoopbackTempPath = repairedLoop ?? string.Empty,
            DetectedAt = recoverable.DetectedAt,
            MicrophoneFileSize = repairedMic is null ? 0 : new FileInfo(repairedMic).Length,
            LoopbackFileSize = repairedLoop is null ? 0 : new FileInfo(repairedLoop).Length,
            HasValidMicrophoneFile = repairedMic is not null,
            HasValidLoopbackFile = repairedLoop is not null
        };
    }

    public void DeleteRecoverable(RecoverableRecording recoverable)
    {
        TryDelete(recoverable.MicrophoneTempPath);
        TryDelete(recoverable.LoopbackTempPath);
        TryDelete(Path.Combine(AppPaths.TempDirectory, $"{recoverable.RecordingId:N}_microphone.recovered.wav"));
        TryDelete(Path.Combine(AppPaths.TempDirectory, $"{recoverable.RecordingId:N}_loopback.recovered.wav"));
        var mixed = Path.Combine(AppPaths.TempDirectory, $"{recoverable.RecordingId:N}_mixed.tmp.wav");
        TryDelete(mixed);
        _manifestStore.Delete(recoverable.RecordingId);
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

    private static DateTimeOffset GetOldestWriteTime(string? first, string? second)
    {
        var dates = new[] { first, second }
            .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
            .Select(path => new DateTimeOffset(File.GetLastWriteTime(path!)))
            .ToArray();
        return dates.Length == 0 ? DateTimeOffset.Now : dates.Min();
    }
}
