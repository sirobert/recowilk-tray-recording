using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using Moq;

namespace MeetingAudioRecorder.Core.Tests;

public class SettingsTransactionServiceTests
{
    [Fact]
    public void HotkeyConflict_DoesNotSaveCandidateSettings()
    {
        var current = CreateSettings("R");
        var candidate = CreateSettings("T");
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Current).Returns(current);
        var hotkey = new Mock<IHotkeyService>();
        hotkey.SetupGet(service => service.IsRegistered).Returns(true);
        hotkey.Setup(service => service.Register(candidate.Hotkey)).Returns(false);
        hotkey.SetupGet(service => service.LastError).Returns("Skrót jest zajęty.");

        var result = SettingsTransactionService.TryCommit(settings.Object, hotkey.Object, candidate);

        Assert.False(result.Success);
        Assert.Equal("Skrót jest zajęty.", result.ErrorMessage);
        settings.Verify(service => service.Save(It.IsAny<AppSettings>()), Times.Never);
        hotkey.Verify(service => service.Register(current.Hotkey), Times.Never);
    }

    [Fact]
    public void SettingsSaveFailure_RestoresPreviousRegisteredHotkey()
    {
        var current = CreateSettings("R");
        var candidate = CreateSettings("T");
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Current).Returns(current);
        settings.Setup(service => service.Save(candidate)).Throws(new IOException("disk"));
        var hotkey = new Mock<IHotkeyService>();
        hotkey.SetupGet(service => service.IsRegistered).Returns(true);
        hotkey.SetupSequence(service => service.Register(It.IsAny<HotkeySettings>()))
            .Returns(true)
            .Returns(true);

        var result = SettingsTransactionService.TryCommit(settings.Object, hotkey.Object, candidate);

        Assert.False(result.Success);
        Assert.Contains("disk", result.ErrorMessage);
        hotkey.Verify(service => service.Register(candidate.Hotkey), Times.Once);
        hotkey.Verify(
            service => service.Register(It.Is<HotkeySettings>(
                value => value.EqualsHotkey(current.Hotkey))),
            Times.Once);
    }

    [Fact]
    public void SuccessfulRegistration_SavesCandidateAfterHotkeyIsActive()
    {
        var current = CreateSettings("R");
        var candidate = CreateSettings("T");
        var calls = new List<string>();
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Current).Returns(current);
        settings.Setup(service => service.Save(candidate)).Callback(() => calls.Add("save"));
        var hotkey = new Mock<IHotkeyService>();
        hotkey.SetupGet(service => service.IsRegistered).Returns(true);
        hotkey.Setup(service => service.Register(candidate.Hotkey))
            .Callback(() => calls.Add("register"))
            .Returns(true);

        var result = SettingsTransactionService.TryCommit(settings.Object, hotkey.Object, candidate);

        Assert.True(result.Success);
        Assert.Equal(["register", "save"], calls);
    }

    [Fact]
    public void UnchangedActiveHotkey_DoesNotRegisterAgain()
    {
        var current = CreateSettings("R");
        var candidate = CreateSettings("R");
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(service => service.Current).Returns(current);
        var hotkey = new Mock<IHotkeyService>();
        hotkey.SetupGet(service => service.IsRegistered).Returns(true);

        var result = SettingsTransactionService.TryCommit(settings.Object, hotkey.Object, candidate);

        Assert.True(result.Success);
        hotkey.Verify(service => service.Register(It.IsAny<HotkeySettings>()), Times.Never);
        settings.Verify(service => service.Save(candidate), Times.Once);
    }

    private static AppSettings CreateSettings(string key)
    {
        var settings = AppSettings.CreateDefault();
        settings.Hotkey.Key = key;
        return settings;
    }
}
