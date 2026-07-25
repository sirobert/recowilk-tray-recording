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
        AppSettings? settings = null,
        Mock<IAudioMixingService>? mixing = null,
        Mock<IMp3EncodingService>? encoding = null)
    {
        if (settings is null)
        {
            settings = AppSettings.CreateDefault();
            settings.MicrophoneDeviceId = "mic-1";
            settings.OutputDeviceId = "out-1";
            settings.RecordingsDirectory = Path.Combine(
                Path.GetTempPath(),
                "mar-coord-" + Guid.NewGuid().ToString("N"));
        }

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

        if (mixing is null)
        {
            mixing = new Mock<IAudioMixingService>();
            mixing.Setup(m => m.MixToWavAsync(It.IsAny<MixRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        if (encoding is null)
        {
            encoding = new Mock<IMp3EncodingService>();
            encoding.Setup(e => e.EncodeToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns<string, string, int, CancellationToken>((_, outPath, _, _) =>
                {
                    File.WriteAllBytes(outPath, new byte[256]);
                    return Task.CompletedTask;
                });
        }

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
    public async Task SettingsChangedDuringRecording_DoNotAffectActiveSession()
    {
        var originalDirectory = Path.Combine(Path.GetTempPath(), "mar-snapshot-" + Guid.NewGuid().ToString("N"));
        var changedDirectory = Path.Combine(Path.GetTempPath(), "mar-snapshot-changed-" + Guid.NewGuid().ToString("N"));
        var settings = AppSettings.CreateDefault();
        settings.MicrophoneDeviceId = "mic-1";
        settings.OutputDeviceId = "out-1";
        settings.RecordingsDirectory = originalDirectory;
        settings.TargetSampleRate = 44100;
        settings.Mp3BitrateKbps = 128;
        settings.MicrophoneVolume = 0.6;
        settings.LoopbackVolume = 0.7;
        settings.KeepSeparateTracks = false;
        settings.FileNameFormat = "Snapshot_yyyy-MM-dd_HH-mm-ss.mp3";

        MixRequest? capturedMix = null;
        int? capturedBitrate = null;
        var mixing = new Mock<IAudioMixingService>();
        mixing.Setup(m => m.MixToWavAsync(It.IsAny<MixRequest>(), It.IsAny<CancellationToken>()))
            .Callback<MixRequest, CancellationToken>((request, _) => capturedMix = request)
            .Returns(Task.CompletedTask);
        var encoding = new Mock<IMp3EncodingService>();
        encoding.Setup(e => e.EncodeToMp3Async(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, int, CancellationToken>((_, output, bitrate, _) =>
            {
                capturedBitrate = bitrate;
                File.WriteAllBytes(output, new byte[256]);
            })
            .Returns(Task.CompletedTask);

        await using var coordinator = CreateCoordinator(
            settings: settings,
            mixing: mixing,
            encoding: encoding);
        await coordinator.StartRecordingAsync();
        var session = coordinator.CurrentSession!;

        settings.MicrophoneDeviceId = "mic-2";
        settings.OutputDeviceId = "out-2";
        settings.RecordingsDirectory = changedDirectory;
        settings.TargetSampleRate = 48000;
        settings.Mp3BitrateKbps = 320;
        settings.MicrophoneVolume = 1.5;
        settings.LoopbackVolume = 1.6;
        settings.KeepSeparateTracks = true;
        settings.FileNameFormat = "Changed_yyyy-MM-dd_HH-mm-ss.mp3";

        File.WriteAllBytes(session.MicrophoneTempPath, new byte[100]);
        File.WriteAllBytes(session.LoopbackTempPath, new byte[100]);
        var result = await coordinator.StopRecordingAsync();

        Assert.True(result.Success);
        Assert.StartsWith(originalDirectory, result.OutputPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("mic-1", session.MicrophoneDeviceId);
        Assert.Equal("out-1", session.OutputDeviceId);
        Assert.Equal(originalDirectory, session.SettingsSnapshot.RecordingsDirectory);
        Assert.NotNull(capturedMix);
        Assert.Equal(44100, capturedMix.TargetSampleRate);
        Assert.Equal(0.6, capturedMix.MicrophoneVolume);
        Assert.Equal(0.7, capturedMix.LoopbackVolume);
        Assert.False(capturedMix.KeepSeparateTracks);
        Assert.Equal(128, capturedBitrate);

        try { Directory.Delete(originalDirectory, recursive: true); }
        catch { /* best effort */ }
        try { Directory.Delete(changedDirectory, recursive: true); }
        catch { /* best effort */ }
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

    [Fact]
    public async Task DisposeAsync_CanBeCalledMoreThanOnce()
    {
        var coordinator = CreateCoordinator();

        await coordinator.DisposeAsync();
        await coordinator.DisposeAsync();
    }
}
