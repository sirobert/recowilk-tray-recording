using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Services;

public static class BrowserAudioDeviceSelector
{
    private static readonly HashSet<string> SupportedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera",
        "vivaldi"
    };

    public static BrowserAudioDeviceSelection Select(
        IEnumerable<BrowserAudioSessionCandidate> candidates,
        string? savedMicrophoneDeviceId,
        string? savedOutputDeviceId)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var active = candidates
            .Where(candidate => candidate.IsActive && SupportedProcesses.Contains(candidate.ProcessName))
            .ToArray();

        var microphone = SelectBest(
            active.Where(candidate => candidate.DeviceType == AudioDeviceType.Capture),
            savedMicrophoneDeviceId);
        var browser = microphone?.ProcessName;

        var outputCandidates = active
            .Where(candidate => candidate.DeviceType == AudioDeviceType.Render)
            .ToArray();
        if (browser is not null)
        {
            outputCandidates = outputCandidates.Where(candidate =>
                    string.Equals(candidate.ProcessName, browser, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        else if (outputCandidates
                     .Select(candidate => candidate.ProcessName)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(2)
                     .Count() > 1)
        {
            outputCandidates = [];
        }

        var output = SelectBest(outputCandidates, savedOutputDeviceId);
        browser ??= output?.ProcessName;

        return new BrowserAudioDeviceSelection(
            microphone?.DeviceId,
            output?.DeviceId,
            browser,
            microphone?.DeviceFriendlyName,
            output?.DeviceFriendlyName);
    }

    private static BrowserAudioSessionCandidate? SelectBest(
        IEnumerable<BrowserAudioSessionCandidate> candidates,
        string? savedDeviceId)
        => candidates
            .OrderByDescending(candidate => candidate.PeakValue > 0.0001f)
            .ThenByDescending(candidate => candidate.PeakValue)
            .ThenByDescending(candidate => string.Equals(
                candidate.DeviceId,
                savedDeviceId,
                StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(candidate => candidate.IsDefaultCommunications)
            .ThenBy(candidate => candidate.DeviceId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
}
