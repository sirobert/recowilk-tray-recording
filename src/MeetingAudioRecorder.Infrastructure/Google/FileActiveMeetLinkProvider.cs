using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.Infrastructure.Google;

public sealed class FileActiveMeetLinkProvider : IActiveMeetLinkProvider, IDisposable
{
    private static readonly TimeSpan MaximumStateAge = TimeSpan.FromSeconds(90);
    private readonly ILogger<FileActiveMeetLinkProvider> _logger;
    private readonly FileSystemWatcher? _watcher;

    public FileActiveMeetLinkProvider(ILogger<FileActiveMeetLinkProvider> logger)
    {
        _logger = logger;
        try
        {
            Directory.CreateDirectory(AppPaths.BrowserDirectory);
            _watcher = new FileSystemWatcher(AppPaths.BrowserDirectory, Path.GetFileName(AppPaths.BrowserStatePath))
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnStateChanged;
            _watcher.Created += OnStateChanged;
            _watcher.Deleted += OnStateChanged;
            _watcher.Renamed += OnStateRenamed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Rozszerzenie Meet nie może monitorować lokalnego folderu stanu");
        }
    }

    public event EventHandler? ActiveLinksChanged;

    public async Task<IReadOnlyList<BrowserMeetLink>> GetActiveLinksAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AppPaths.BrowserStatePath))
            return [];

        try
        {
            await using var stream = new FileStream(
                AppPaths.BrowserStatePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            return BrowserMeetStateParser.ParseFresh(json, DateTimeOffset.UtcNow, MaximumStateAge);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Nie można odczytać stanu rozszerzenia Meet");
            return [];
        }
    }

    public void Dispose()
    {
        if (_watcher is null)
            return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnStateChanged;
        _watcher.Created -= OnStateChanged;
        _watcher.Deleted -= OnStateChanged;
        _watcher.Renamed -= OnStateRenamed;
        _watcher.Dispose();
    }

    private void OnStateChanged(object sender, FileSystemEventArgs e)
        => ActiveLinksChanged?.Invoke(this, EventArgs.Empty);

    private void OnStateRenamed(object sender, RenamedEventArgs e)
        => ActiveLinksChanged?.Invoke(this, EventArgs.Empty);
}
