using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;

namespace MeetingAudioRecorder.Core.Tests;

public class SettingsValidatorTests
{
    [Fact]
    public void Validate_DefaultSettings_IsValid()
    {
        var settings = AppSettings.CreateDefault();
        var result = SettingsValidator.Validate(settings);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(192)]
    [InlineData(256)]
    [InlineData(320)]
    public void Validate_AllowedBitrates_Ok(int bitrate)
    {
        var s = AppSettings.CreateDefault();
        s.Mp3BitrateKbps = bitrate;
        Assert.True(SettingsValidator.Validate(s).IsValid);
    }

    [Fact]
    public void Validate_InvalidBitrate_Fails()
    {
        var s = AppSettings.CreateDefault();
        s.Mp3BitrateKbps = 64;
        var r = SettingsValidator.Validate(s);
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("bitrate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EmptyDirectory_Fails()
    {
        var s = AppSettings.CreateDefault();
        s.RecordingsDirectory = "  ";
        Assert.False(SettingsValidator.Validate(s).IsValid);
    }

    [Fact]
    public void Validate_HotkeyWithoutModifier_Fails()
    {
        var s = AppSettings.CreateDefault();
        s.Hotkey = new HotkeySettings { Key = "R", Control = false, Alt = false, Shift = false, Windows = false };
        Assert.False(SettingsValidator.Validate(s).IsValid);
    }

    [Fact]
    public void Validate_VolumeOutOfRange_Fails()
    {
        var s = AppSettings.CreateDefault();
        s.MicrophoneVolume = 5;
        Assert.False(SettingsValidator.Validate(s).IsValid);
    }

    [Fact]
    public void Sanitize_FixesInvalidValues()
    {
        var s = AppSettings.CreateDefault();
        s.Mp3BitrateKbps = 99;
        s.TargetSampleRate = 22050;
        s.MicrophoneVolume = 9;
        s.RecordingsDirectory = "";
        var fixedSettings = SettingsValidator.Sanitize(s);
        Assert.Equal(192, fixedSettings.Mp3BitrateKbps);
        Assert.Equal(48000, fixedSettings.TargetSampleRate);
        Assert.InRange(fixedSettings.MicrophoneVolume, 0, 2);
        Assert.False(string.IsNullOrWhiteSpace(fixedSettings.RecordingsDirectory));
    }
}
