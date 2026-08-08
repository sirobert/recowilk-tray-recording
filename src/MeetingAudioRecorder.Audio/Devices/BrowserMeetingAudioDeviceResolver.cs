using System.Diagnostics;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MeetingAudioRecorder.Audio.Devices;

public sealed class BrowserMeetingAudioDeviceResolver : IMeetingAudioDeviceResolver
{
    private readonly ILogger<BrowserMeetingAudioDeviceResolver> _logger;

    public BrowserMeetingAudioDeviceResolver(ILogger<BrowserMeetingAudioDeviceResolver> logger)
    {
        _logger = logger;
    }

    public BrowserAudioDeviceSelection DetectActiveBrowserDevices(
        string? savedMicrophoneDeviceId,
        string? savedOutputDeviceId)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultCaptureId = GetDefaultCommunicationsId(enumerator, DataFlow.Capture);
            var defaultRenderId = GetDefaultCommunicationsId(enumerator, DataFlow.Render);
            var candidates = new List<BrowserAudioSessionCandidate>();

            CollectCandidates(
                enumerator,
                DataFlow.Capture,
                AudioDeviceType.Capture,
                defaultCaptureId,
                candidates);
            CollectCandidates(
                enumerator,
                DataFlow.Render,
                AudioDeviceType.Render,
                defaultRenderId,
                candidates);

            var selection = BrowserAudioDeviceSelector.Select(
                candidates,
                savedMicrophoneDeviceId,
                savedOutputDeviceId);
            if (selection.HasDetectedDevice)
            {
                _logger.LogInformation(
                    "Wykryto aktywne sesje audio przeglądarki {Browser}; mic={HasMic}, out={HasOutput}",
                    selection.BrowserProcessName,
                    selection.MicrophoneDeviceId is not null,
                    selection.OutputDeviceId is not null);
            }
            else
            {
                _logger.LogInformation(
                    "Nie wykryto aktywnych sesji audio obsługiwanej przeglądarki; użyto zapisanych urządzeń");
            }

            return selection;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się przeskanować sesji audio przeglądarek");
            return new BrowserAudioDeviceSelection(null, null, null);
        }
    }

    private void CollectCandidates(
        MMDeviceEnumerator enumerator,
        DataFlow flow,
        AudioDeviceType deviceType,
        string? defaultCommunicationsId,
        ICollection<BrowserAudioSessionCandidate> candidates)
    {
        var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
        {
            using var device = devices[deviceIndex];
            try
            {
                var manager = device.AudioSessionManager;
                try
                {
                    manager.RefreshSessions();
                    var sessions = manager.Sessions;
                    for (var sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
                    {
                        using var session = sessions[sessionIndex];
                        var processName = TryGetProcessName(session.GetProcessID);
                        if (processName is null)
                            continue;

                        var peak = TryGetPeak(session);
                        candidates.Add(new BrowserAudioSessionCandidate(
                            device.ID,
                            deviceType,
                            processName,
                            session.State == AudioSessionState.AudioSessionStateActive,
                        peak,
                        string.Equals(device.ID, defaultCommunicationsId, StringComparison.OrdinalIgnoreCase),
                        device.FriendlyName));
                    }
                }
                finally
                {
                    manager.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Nie można odczytać sesji endpointu audio typu {DeviceType}", deviceType);
            }
        }
    }

    private static string? GetDefaultCommunicationsId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Communications);
            return device.State == DeviceState.Active ? device.ID : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetProcessName(uint processId)
    {
        if (processId == 0 || processId > int.MaxValue)
            return null;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static float TryGetPeak(AudioSessionControl session)
    {
        try
        {
            return session.AudioMeterInformation.MasterPeakValue;
        }
        catch
        {
            return 0;
        }
    }
}
