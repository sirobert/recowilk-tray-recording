using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using Moq;

namespace MeetingAudioRecorder.Core.Tests;

public sealed class RecowilkSettingsTransactionServiceTests
{
    [Fact]
    public async Task Invalid_new_key_does_not_change_settings_or_previous_secret()
    {
        var settings = Settings();
        var credentials = new Mock<IRecowilkCredentialStore>();
        credentials.SetupGet(x => x.HasKey).Returns(true);
        credentials.Setup(x => x.Load()).Returns("old-key");
        var queue = new Mock<IRecowilkUploadQueue>();
        queue.Setup(x => x.TestConnectionAsync("https://recowilk.example", "new-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RecowilkConnectionResult.Invalid(RecowilkConnectionFailure.Unauthorized));

        var result = await RecowilkSettingsTransactionService.TryCommitAsync(settings.Object, Hotkey().Object,
            credentials.Object, queue.Object, Candidate(), "new-key");

        Assert.False(result.Success);
        settings.Verify(x => x.Save(It.IsAny<AppSettings>()), Times.Never);
        credentials.Verify(x => x.Save(It.IsAny<string>()), Times.Never);
        credentials.Verify(x => x.Clear(), Times.Never);
    }

    [Fact]
    public async Task Settings_failure_restores_previous_secret()
    {
        var settings = Settings();
        settings.Setup(x => x.Save(It.IsAny<AppSettings>())).Throws(new IOException("disk"));
        var credentials = new Mock<IRecowilkCredentialStore>();
        credentials.SetupGet(x => x.HasKey).Returns(true);
        credentials.Setup(x => x.Load()).Returns("old-key");
        var queue = new Mock<IRecowilkUploadQueue>();
        queue.Setup(x => x.TestConnectionAsync("https://recowilk.example", "new-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecowilkConnectionResult(true));

        var result = await RecowilkSettingsTransactionService.TryCommitAsync(settings.Object, Hotkey().Object,
            credentials.Object, queue.Object, Candidate(), "new-key");

        Assert.False(result.Success);
        credentials.Verify(x => x.Save("new-key"), Times.Once);
        credentials.Verify(x => x.Save("old-key"), Times.Once);
    }

    private static AppSettings Candidate() => new()
    {
        RecowilkUploadEnabled = true,
        RecowilkBaseUrl = "https://recowilk.example",
        Hotkey = new HotkeySettings { Key = "R", Control = true }
    };

    private static Mock<ISettingsService> Settings()
    {
        var mock = new Mock<ISettingsService>();
        mock.SetupGet(x => x.Current).Returns(AppSettings.CreateDefault());
        mock.Setup(x => x.Validate(It.IsAny<AppSettings>())).Returns(ValidationResult.Success());
        return mock;
    }

    private static Mock<IHotkeyService> Hotkey()
    {
        var mock = new Mock<IHotkeyService>();
        mock.SetupGet(x => x.IsRegistered).Returns(true);
        mock.Setup(x => x.Register(It.IsAny<HotkeySettings>())).Returns(true);
        return mock;
    }
}
