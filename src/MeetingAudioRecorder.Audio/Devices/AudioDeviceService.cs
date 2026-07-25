using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MeetingAudioRecorder.Audio.Devices;

public sealed class AudioDeviceService : IAudioDeviceService
{
    private readonly ILogger<AudioDeviceService> _logger;
    private readonly MMDeviceEnumerator _enumerator;
    private NotificationClient? _notificationClient;
    private bool _disposed;

    public AudioDeviceService(ILogger<AudioDeviceService> logger)
    {
        _logger = logger;
        _enumerator = new MMDeviceEnumerator();
    }

    public event EventHandler<DeviceChangedEventArgs>? DeviceChanged;

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
        => Enumerate(DataFlow.Capture);

    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
        => Enumerate(DataFlow.Render);

    public AudioDeviceInfo? FindDeviceById(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        try
        {
            using var device = _enumerator.GetDevice(deviceId);
            if (device.State != DeviceState.Active)
                return null;
            return ToInfo(device);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Nie znaleziono urządzenia {Id}", deviceId);
            return null;
        }
    }

    public AudioDeviceInfo? GetDefaultCaptureDevice(bool communications = true)
    {
        try
        {
            var role = communications ? Role.Communications : Role.Multimedia;
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
            return device.State == DeviceState.Active ? ToInfo(device) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brak domyślnego mikrofonu");
            return null;
        }
    }

    public AudioDeviceInfo? GetDefaultRenderDevice(bool communications = true)
    {
        try
        {
            var role = communications ? Role.Communications : Role.Multimedia;
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
            return device.State == DeviceState.Active ? ToInfo(device) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brak domyślnego urządzenia wyjściowego");
            return null;
        }
    }

    public DeviceResolutionResult ResolveDevice(string? savedDeviceId, AudioDeviceType type)
    {
        if (!string.IsNullOrWhiteSpace(savedDeviceId))
        {
            var found = FindDeviceById(savedDeviceId);
            if (found is not null)
                return new DeviceResolutionResult { Device = found, UsedFallback = false };
        }

        var fallback = type == AudioDeviceType.Capture
            ? GetDefaultCaptureDevice(communications: true) ?? GetDefaultCaptureDevice(communications: false)
            : GetDefaultRenderDevice(communications: true) ?? GetDefaultRenderDevice(communications: false);

        if (fallback is null)
        {
            var label = type == AudioDeviceType.Capture ? "mikrofon" : "urządzenie wyjściowe";
            return new DeviceResolutionResult
            {
                Device = null,
                UsedFallback = true,
                WarningMessage = $"Zapisane {label} nie jest dostępne i nie znaleziono urządzenia domyślnego. Wybierz urządzenie w ustawieniach."
            };
        }

        var kind = type == AudioDeviceType.Capture ? "mikrofon" : "urządzenie wyjściowe";
        return new DeviceResolutionResult
        {
            Device = fallback,
            UsedFallback = true,
            WarningMessage = $"Zapisane {kind} nie jest dostępne. Użyto domyślnego urządzenia komunikacyjnego: {fallback.FriendlyName}."
        };
    }

    public void StartWatching()
    {
        if (_notificationClient is not null)
            return;

        _notificationClient = new NotificationClient(this);
        try
        {
            _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
            _logger.LogInformation("Rozpoczęto monitorowanie zmian urządzeń audio");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nie udało się zarejestrować powiadomień o urządzeniach");
            _notificationClient = null;
        }
    }

    public void StopWatching()
    {
        if (_notificationClient is null)
            return;

        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "UnregisterEndpointNotificationCallback");
        }

        _notificationClient = null;
    }

    private List<AudioDeviceInfo> Enumerate(DataFlow flow)
    {
        var list = new List<AudioDeviceInfo>();
        try
        {
            var devices = _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            string? defaultId = null;
            string? defaultCommId = null;
            try
            {
                using var d = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
                defaultId = d.ID;
            }
            catch { /* brak domyślnego */ }

            try
            {
                using var d = _enumerator.GetDefaultAudioEndpoint(flow, Role.Communications);
                defaultCommId = d.ID;
            }
            catch { /* brak */ }

            for (var i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                try
                {
                    var info = ToInfo(device, defaultId, defaultCommId);
                    list.Add(info);
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd enumeracji urządzeń {Flow}", flow);
        }

        return list.OrderBy(d => d.FriendlyName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static AudioDeviceInfo ToInfo(MMDevice device, string? defaultId = null, string? defaultCommId = null)
    {
        return new AudioDeviceInfo
        {
            Id = device.ID,
            FriendlyName = device.FriendlyName,
            Description = device.DeviceFriendlyName,
            DeviceType = device.DataFlow == DataFlow.Capture ? AudioDeviceType.Capture : AudioDeviceType.Render,
            IsActive = device.State == DeviceState.Active,
            IsDefault = defaultId is not null && device.ID == defaultId,
            IsDefaultCommunications = defaultCommId is not null && device.ID == defaultCommId
        };
    }

    internal void RaiseDeviceChanged(string deviceId, DeviceChangeKind kind, string? message = null)
    {
        _logger.LogInformation("Zmiana urządzenia: {Kind} {Id} {Msg}", kind, deviceId, message);
        DeviceChanged?.Invoke(this, new DeviceChangedEventArgs(deviceId, kind, message));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
        _enumerator.Dispose();
    }

    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly AudioDeviceService _owner;

        public NotificationClient(AudioDeviceService owner) => _owner = owner;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            var msg = newState == DeviceState.Active
                ? "Urządzenie aktywne"
                : $"Stan urządzenia: {newState}";
            _owner.RaiseDeviceChanged(deviceId, DeviceChangeKind.StateChanged, msg);
        }

        public void OnDeviceAdded(string deviceId)
            => _owner.RaiseDeviceChanged(deviceId, DeviceChangeKind.Added, "Dodano urządzenie");

        public void OnDeviceRemoved(string deviceId)
            => _owner.RaiseDeviceChanged(deviceId, DeviceChangeKind.Removed, "Usunięto urządzenie");

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
            => _owner.RaiseDeviceChanged(defaultDeviceId, DeviceChangeKind.DefaultChanged,
                $"Zmieniono domyślne urządzenie ({flow}/{role})");

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
            => _owner.RaiseDeviceChanged(deviceId, DeviceChangeKind.PropertyChanged);
    }
}
