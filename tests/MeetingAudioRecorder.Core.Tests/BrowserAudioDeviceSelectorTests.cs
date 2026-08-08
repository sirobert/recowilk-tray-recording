using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;

namespace MeetingAudioRecorder.Core.Tests;

public sealed class BrowserAudioDeviceSelectorTests
{
    [Fact]
    public void ActiveBrowserCapture_DeterminesBrowserFamilyAndMatchingOutput()
    {
        BrowserAudioSessionCandidate[] candidates =
        [
            new("mic-chrome", AudioDeviceType.Capture, "chrome", true, 0.02f, false),
            new("out-chrome", AudioDeviceType.Render, "chrome", true, 0.10f, false),
            new("out-edge", AudioDeviceType.Render, "msedge", true, 0.90f, false)
        ];

        var result = BrowserAudioDeviceSelector.Select(candidates, "saved-mic", "saved-out");

        Assert.Equal("mic-chrome", result.MicrophoneDeviceId);
        Assert.Equal("out-chrome", result.OutputDeviceId);
        Assert.Equal("chrome", result.BrowserProcessName);
    }

    [Fact]
    public void SilentAmbiguousSessions_PreferSavedThenDefaultCommunicationDevice()
    {
        BrowserAudioSessionCandidate[] candidates =
        [
            new("mic-default", AudioDeviceType.Capture, "msedge", true, 0, true),
            new("mic-saved", AudioDeviceType.Capture, "msedge", true, 0, false),
            new("out-default", AudioDeviceType.Render, "msedge", true, 0, true),
            new("out-saved", AudioDeviceType.Render, "msedge", true, 0, false)
        ];

        var saved = BrowserAudioDeviceSelector.Select(candidates, "mic-saved", "out-saved");
        var defaults = BrowserAudioDeviceSelector.Select(candidates, "missing", "missing");

        Assert.Equal("mic-saved", saved.MicrophoneDeviceId);
        Assert.Equal("out-saved", saved.OutputDeviceId);
        Assert.Equal("mic-default", defaults.MicrophoneDeviceId);
        Assert.Equal("out-default", defaults.OutputDeviceId);
    }

    [Fact]
    public void InactiveAndUnsupportedSessions_DoNotOverrideSavedDevices()
    {
        BrowserAudioSessionCandidate[] candidates =
        [
            new("mic-inactive", AudioDeviceType.Capture, "chrome", false, 1, true),
            new("out-player", AudioDeviceType.Render, "vlc", true, 1, true)
        ];

        var result = BrowserAudioDeviceSelector.Select(candidates, "saved-mic", "saved-out");

        Assert.Null(result.MicrophoneDeviceId);
        Assert.Null(result.OutputDeviceId);
        Assert.Null(result.BrowserProcessName);
    }

    [Fact]
    public void ApplyRecordingSelection_OverridesCloneWithoutChangingSavedSettings()
    {
        var settings = AppSettings.CreateDefault();
        settings.MicrophoneDeviceId = "saved-mic";
        settings.OutputDeviceId = "saved-out";
        var selection = new RecordingDeviceSelection("browser-mic", null, "chrome");

        var effective = RecordingDeviceSelectionResolver.Apply(settings, selection);

        Assert.Equal("browser-mic", effective.MicrophoneDeviceId);
        Assert.Equal("saved-out", effective.OutputDeviceId);
        Assert.Equal("saved-mic", settings.MicrophoneDeviceId);
        Assert.Equal("saved-out", settings.OutputDeviceId);
    }

    [Fact]
    public void MissingCaptureWithMultipleBrowserFamilies_DoesNotGuessMeetingOutput()
    {
        BrowserAudioSessionCandidate[] candidates =
        [
            new("out-chrome", AudioDeviceType.Render, "chrome", true, 0.20f, false),
            new("out-edge", AudioDeviceType.Render, "msedge", true, 0.90f, true)
        ];

        var result = BrowserAudioDeviceSelector.Select(candidates, "saved-mic", "saved-out");

        Assert.Null(result.MicrophoneDeviceId);
        Assert.Null(result.OutputDeviceId);
        Assert.Null(result.BrowserProcessName);
    }
}
