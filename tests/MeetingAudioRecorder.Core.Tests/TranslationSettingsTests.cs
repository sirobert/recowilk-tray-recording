using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
namespace MeetingAudioRecorder.Core.Tests;

public class TranslationSettingsTests
{
    [Fact]
    public void OldSettingsGetIndependentDefaultTranslationConfiguration()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.True(settings.Translation.OutputEnabled); Assert.False(settings.Translation.MicrophoneEnabled);
        var copy = settings.Clone(); copy.Translation.Hotkey.Key = "X";
        Assert.Equal("T", settings.Translation.Hotkey.Key);
    }
    [Fact]
    public void ConflictingHotkeysAndInvalidAppearanceAreRejected()
    {
        var settings = AppSettings.CreateDefault(); settings.Translation.Hotkey = settings.Hotkey.Clone();
        Assert.False(SettingsValidator.Validate(settings).IsValid);
        settings.Translation.Hotkey.Key = "T"; settings.Translation.FontSize = double.NaN;
        Assert.False(SettingsValidator.Validate(settings).IsValid);
        settings.Translation.FontSize = 14; settings.Translation.Foreground = "invalid";
        Assert.False(SettingsValidator.Validate(settings).IsValid);
    }
    [Fact]
    public void TranslationShortcutRollsBackOnSaveFailureWithoutChangingRecordingShortcut()
    {
        var current = AppSettings.CreateDefault(); var candidate = current.Clone(); candidate.Translation.Hotkey.Key = "Y";
        var settings = new Moq.Mock<MeetingAudioRecorder.Core.Interfaces.ISettingsService>();
        settings.SetupGet(x => x.Current).Returns(current); settings.Setup(x => x.Save(candidate)).Throws(new IOException("disk"));
        var hotkey = new Moq.Mock<MeetingAudioRecorder.Core.Interfaces.IHotkeyService>();
        hotkey.SetupGet(x => x.IsRegistered).Returns(true); hotkey.Setup(x => x.Register(Moq.It.IsAny<HotkeySettings>())).Returns(true);
        Assert.False(SettingsTransactionService.TryCommit(settings.Object, hotkey.Object, candidate, translation: true).Success);
        hotkey.Verify(x => x.Register(Moq.It.Is<HotkeySettings>(h => h.Key == "T")), Moq.Times.Once);
        hotkey.Verify(x => x.Register(Moq.It.Is<HotkeySettings>(h => h.Key == "R")), Moq.Times.Never);
        Assert.Equal("R", current.Hotkey.Key);
    }
    [Fact]
    public void AtLeastOneTranslationSourceIsRequired()
    {
        var settings = AppSettings.CreateDefault(); settings.Translation.OutputEnabled = false;
        Assert.False(SettingsValidator.Validate(settings).IsValid);
        settings.Translation.MicrophoneEnabled = true; Assert.True(SettingsValidator.Validate(settings).IsValid);
    }
}
