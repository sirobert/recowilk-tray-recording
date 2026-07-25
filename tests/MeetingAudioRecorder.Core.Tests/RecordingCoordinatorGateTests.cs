using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MeetingAudioRecorder.Core.Tests;

/// <summary>
/// Testy blokowania podwójnego start/stop bez fizycznych urządzeń.
/// </summary>
public class RecordingCoordinatorGateTests
{
    private static RecordingCoordinator CreateCoordinator(
        Mock<IMicrophoneCaptureService>? mic = null,
        Mock<ILoopbackCaptureService>? loop = null,
        AppSettings? settings = null)
    {
        settings ??= AppSettings.CreateDefault();
        settings.MicrophoneDeviceId = "mic-1";
        settings.OutputDeviceId = "out-1";
        settings.RecordingsDirectory = Path.Combine(Path.GetTempPath(), "mar-coord-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(settings.RecordingsDirectory);

        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.Current).Returns(settings);
        settingsService.Setup(s => s.Validate(It.IsAny<AppSettings>()))
            .Returns(ValidationResult.Success());

        var deviceService = new Mock<IAudioDeviceService>();
        deviceService.Setup(d => d.ResolveDevice("mic-1", AudioDeviceType.Capture))
            .Returns(new DeviceResolutionResult
            {
                Device = new AudioDeviceInfo { Id = "mic-1", FriendlyName = "Mic", DeviceType = AudioDeviceType.Capture, IsActive = true }
            });
        deviceService.Setup(d => d.ResolveDevice("out-1", AudioDeviceType.Render))
            .Returns(new DeviceResolutionResult
            {
                Device = new AudioDeviceInfo { Id = "out-1", FriendlyName = "Out", DeviceType = AudioDeviceType.Render, IsActive = true }
            });

        var createdMic = mic is null;
        var createdLoop = loop is null;
        mic ??= new Mock<IMicrophoneCaptureService>();
        loop ??= new Mock<ILoopbackCaptureService>();
        if (createdMic)
        {
            mic.Setup(m => m.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        mic.Setup(m => m.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mic.Setup(m => m.DisposeAsync()).Returns(ValueTask.CompletedTask);
        if (createdLoop)
        {
            loop.Setup(m => m.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        loop.Setup(m => m.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        loop.Setup(m => m.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var mixing = new Mock<IAudioMixingService>();
        mixing.Setup(m => m.MixToWavAsync(It.IsAny<MixRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var encoding = new Mock<IMp3EncodingService>();
        encoding.Setup(e => e.EncodeToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, int, CancellationToken>((_, outPath, _, _) =>
            {
                File.WriteAllBytes(outPath, new byte[256]);
                return Task.CompletedTask;
            });

        var disk = new Mock<IDiskSpaceService>();
        disk.Setup(d => d.HasEnoughSpace(It.IsAny<string>(), It.IsAny<long>(), out It.Ref<long>.IsAny))
            .Returns((string _, long _, out long available) =>
            {
                available = 10L * 1024 * 1024 * 1024;
                return true;
            });

        return new RecordingCoordinator(
            settingsService.Object,
            deviceService.Object,
            () => mic.Object,
            () => loop.Object,
            mixing.Object,
            encoding.Object,
            new FileNameService(),
            disk.Object,
            Mock.Of<IRecordingSessionManifestStore>(),
            NullLogger<RecordingCoordinator>.Instance);
    }

    [Fact]
    public async Task DoubleStart_SecondThrows()
    {
        var mic = new Mock<IMicrophoneCaptureService>();
        var tcs = new TaskCompletionSource();
        mic.Setup(m => m.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () => await tcs.Task);
        mic.Setup(m => m.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mic.Setup(m => m.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var loop = new Mock<ILoopbackCaptureService>();
        loop.Setup(m => m.StartAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () => await tcs.Task);
        loop.Setup(m => m.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        loop.Setup(m => m.DisposeAsync()).Returns(ValueTask.CompletedTask);

        await using var coordinator = CreateCoordinator(mic, loop);

        var start1 = coordinator.StartRecordingAsync();
        await Task.Delay(50);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartRecordingAsync());

        tcs.SetResult();
        await start1;
        Assert.Equal(AppRecordingState.Recording, coordinator.State);

        // cleanup - create dummy temp wavs so process doesn't fail hard
        var session = coordinator.CurrentSession!;
        File.WriteAllBytes(session.MicrophoneTempPath, new byte[100]);
        File.WriteAllBytes(session.LoopbackTempPath, new byte[100]);
        await coordinator.StopRecordingAsync();
    }

    [Fact]
    public async Task MissingDevice_StartFails()
    {
        var settings = AppSettings.CreateDefault();
        settings.MicrophoneDeviceId = "missing";
        settings.OutputDeviceId = "out-1";
        settings.RecordingsDirectory = Path.Combine(Path.GetTempPath(), "mar-" + Guid.NewGuid().ToString("N"));

        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.Current).Returns(settings);

        var deviceService = new Mock<IAudioDeviceService>();
        deviceService.Setup(d => d.ResolveDevice("missing", AudioDeviceType.Capture))
            .Returns(new DeviceResolutionResult
            {
                Device = null,
                UsedFallback = true,
                WarningMessage = "Brak mikrofonu"
            });

        await using var coordinator = new RecordingCoordinator(
            settingsService.Object,
            deviceService.Object,
            () => Mock.Of<IMicrophoneCaptureService>(),
            () => Mock.Of<ILoopbackCaptureService>(),
            Mock.Of<IAudioMixingService>(),
            Mock.Of<IMp3EncodingService>(),
            new FileNameService(),
            Mock.Of<IDiskSpaceService>(d => d.HasEnoughSpace(It.IsAny<string>(), It.IsAny<long>(), out It.Ref<long>.IsAny) == true),
            Mock.Of<IRecordingSessionManifestStore>(),
            NullLogger<RecordingCoordinator>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartRecordingAsync());
    }
}
