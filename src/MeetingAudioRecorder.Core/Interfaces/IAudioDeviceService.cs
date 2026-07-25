using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IAudioDeviceService : IDisposable
{
    event EventHandler<DeviceChangedEventArgs>? DeviceChanged;

    IReadOnlyList<AudioDeviceInfo> GetCaptureDevices();
    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();
    AudioDeviceInfo? FindDeviceById(string deviceId);
    AudioDeviceInfo? GetDefaultCaptureDevice(bool communications = true);
    AudioDeviceInfo? GetDefaultRenderDevice(bool communications = true);
    DeviceResolutionResult ResolveDevice(string? savedDeviceId, AudioDeviceType type);
    void StartWatching();
    void StopWatching();
}

public sealed class DeviceResolutionResult
{
    public required AudioDeviceInfo? Device { get; init; }
    public bool UsedFallback { get; init; }
    public string? WarningMessage { get; init; }
}
