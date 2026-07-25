using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.Infrastructure.SingleInstance;

public sealed class NamedMutexSingleInstanceService : ISingleInstanceService
{
    private readonly ILogger<NamedMutexSingleInstanceService> _logger;
    private Mutex? _mutex;
    private bool _owned;

    public NamedMutexSingleInstanceService(ILogger<NamedMutexSingleInstanceService> logger)
    {
        _logger = logger;
    }

    public event EventHandler? SecondInstanceDetected;

    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, AppPaths.MutexName, out var createdNew);
            _owned = createdNew;
            if (!createdNew)
            {
                _logger.LogInformation("Wykryto drugą instancję — mutex zajęty");
                try { _mutex.Dispose(); } catch { /* ignore */ }
                _mutex = null;
            }
            return createdNew;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd Mutex");
            // W razie błędu pozwól uruchomić (lepsze niż całkowita blokada)
            return true;
        }
    }

    public void SignalFirstInstance()
    {
        // W tej wersji UI App obsługuje aktywację przez event w App.xaml.cs
        SecondInstanceDetected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_mutex is not null && _owned)
        {
            try { _mutex.ReleaseMutex(); } catch { /* ignore */ }
        }
        _mutex?.Dispose();
        _mutex = null;
    }
}
