using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace MeetingAudioRecorder.Core.Tests;

public sealed class MeetingAutomationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DisabledAutomation_DoesNotCallGoogle()
    {
        var fixture = new Fixture(enabled: false);

        await fixture.Service.CheckNowAsync();

        fixture.Authorization.VerifyNoOtherCalls();
        fixture.Calendar.VerifyNoOtherCalls();
        fixture.Meet.VerifyNoOtherCalls();
        fixture.Coordinator.Verify(value => value.StartRecordingAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(MeetingAutomationState.Disabled, fixture.Service.Status.State);
    }

    [Fact]
    public async Task PresentUser_StartsExactlyOneAutomaticRecording()
    {
        var fixture = new Fixture();

        await fixture.Service.CheckNowAsync();
        await fixture.Service.CheckNowAsync();

        fixture.Coordinator.Verify(value => value.StartRecordingWithDevicesAsync(
            It.Is<RecordingDeviceSelection>(selection =>
                selection.MicrophoneDeviceId == "browser-mic"
                && selection.OutputDeviceId == "browser-out"),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Coordinator.Verify(value => value.StopRecordingAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(MeetingAutomationState.RecordingAutomatically, fixture.Service.Status.State);
    }

    [Fact]
    public async Task NoActiveBrowserSessions_StartsWithSavedDeviceFallback()
    {
        var fixture = new Fixture();
        fixture.DeviceResolver.Setup(value => value.DetectActiveBrowserDevices(
                It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new BrowserAudioDeviceSelection(null, null, null));

        await fixture.Service.CheckNowAsync();

        fixture.Coordinator.Verify(value => value.StartRecordingWithDevicesAsync(
            It.Is<RecordingDeviceSelection>(selection =>
                selection.MicrophoneDeviceId == null
                && selection.OutputDeviceId == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActiveMeetLinkWithoutCalendarEvent_StartsAutomaticRecording()
    {
        var fixture = new Fixture();
        fixture.Calendar.Setup(value => value.ListMeetingCandidatesAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        fixture.ActiveMeetLinks.Setup(value => value.GetActiveLinksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new BrowserMeetLink("tcu-ysxp-tvw", "chrome", Now)
            ]);

        await fixture.Service.CheckNowAsync();

        fixture.Meet.Verify(value => value.GetCurrentUserPresenceAsync(
            "tcu-ysxp-tvw",
            "users/me-123",
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Coordinator.Verify(value => value.StartRecordingWithDevicesAsync(
            It.IsAny<RecordingDeviceSelection>(),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Google Meet tcu-ysxp-tvw", fixture.Service.Status.MeetingTitle);
    }

    [Fact]
    public async Task BrowserStateChange_TriggersCheckWithoutWaitingForCalendarPoll()
    {
        var fixture = new Fixture();
        fixture.Calendar.Setup(value => value.ListMeetingCandidatesAsync(
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        fixture.ActiveMeetLinks.Setup(value => value.GetActiveLinksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BrowserMeetLink("tcu-ysxp-tvw", "chrome", Now)]);

        fixture.ActiveMeetLinks.Raise(value => value.ActiveLinksChanged += null, EventArgs.Empty);
        await fixture.AutomaticStartSignal.Task.WaitAsync(TimeSpan.FromSeconds(2));

        fixture.Coordinator.Verify(value => value.StartRecordingWithDevicesAsync(
            It.IsAny<RecordingDeviceSelection>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompletedTrackedMeeting_DoesNotBlockNewActiveBrowserMeeting()
    {
        var fixture = new Fixture();

        await fixture.Service.CheckNowAsync();
        fixture.CompleteRecordingExternally();
        await fixture.Service.CheckNowAsync();

        fixture.PresenceByMeetingCode["abc-defg-hij"] = MeetingPresenceStatus.Absent;
        fixture.PresenceByMeetingCode["tcu-ysxp-tvw"] = MeetingPresenceStatus.Present;
        fixture.ActiveMeetLinks.Setup(value => value.GetActiveLinksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BrowserMeetLink("tcu-ysxp-tvw", "chrome", Now)]);

        await fixture.Service.CheckNowAsync();

        fixture.Meet.Verify(value => value.GetCurrentUserPresenceAsync(
            "tcu-ysxp-tvw",
            "users/me-123",
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Coordinator.Verify(value => value.StartRecordingWithDevicesAsync(
            It.IsAny<RecordingDeviceSelection>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal("Google Meet tcu-ysxp-tvw", fixture.Service.Status.MeetingTitle);
    }

    [Fact]
    public async Task ThreeConfirmedAbsencesAfterGrace_StopOwnedRecording()
    {
        var fixture = new Fixture();
        await fixture.Service.CheckNowAsync();
        fixture.Presence = MeetingPresenceStatus.Absent;

        fixture.Time.Advance(TimeSpan.FromSeconds(5));
        await fixture.Service.CheckNowAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(7));
        await fixture.Service.CheckNowAsync();
        fixture.Time.Advance(TimeSpan.FromSeconds(8));
        await fixture.Service.CheckNowAsync();

        fixture.Coordinator.Verify(value => value.StopRecordingAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AppRecordingState.Completed, fixture.RecordingState);
    }

    [Fact]
    public async Task ApiFailureWhileRecording_DoesNotStopRecording()
    {
        var fixture = new Fixture();
        await fixture.Service.CheckNowAsync();
        fixture.ThrowMeetError = true;

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        await fixture.Service.CheckNowAsync();

        fixture.Coordinator.Verify(value => value.StopRecordingAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(AppRecordingState.Recording, fixture.RecordingState);
        Assert.Equal(MeetingAutomationState.ApiUnavailable, fixture.Service.Status.State);
    }

    private sealed class Fixture
    {
        private readonly AppSettings _settings;
        private RecordingSessionInfo? _session;

        public Fixture(bool enabled = true)
        {
            _settings = AppSettings.CreateDefault();
            _settings.GoogleMeetAutomationEnabled = enabled;
            var settingsService = new Mock<ISettingsService>();
            settingsService.Setup(value => value.Current).Returns(() => _settings.Clone());

            Authorization.Setup(value => value.GetConnectionInfoAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GoogleConnectionInfo(true, "recorder@example.com", "users/me-123"));
            Calendar.Setup(value => value.ListMeetingCandidatesAsync(
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<DateTimeOffset>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(
                [
                    new GoogleCalendarMeeting(
                        "event-1",
                        "Daily",
                        Now.AddMinutes(-5),
                        Now.AddMinutes(25),
                        "https://meet.google.com/abc-defg-hij",
                        "abc-defg-hij")
                ]);
            Meet.Setup(value => value.GetCurrentUserPresenceAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string meetingCode, string _, CancellationToken _) =>
                {
                    if (ThrowMeetError)
                        throw new HttpRequestException("Temporary API failure.");
                    var presence = PresenceByMeetingCode.GetValueOrDefault(meetingCode, Presence);
                    return new GoogleMeetPresence(
                        meetingCode,
                        presence == MeetingPresenceStatus.Present ? "conferenceRecords/conference-1" : null,
                        presence);
                });

            DeviceResolver.Setup(value => value.DetectActiveBrowserDevices(
                    It.IsAny<string>(), It.IsAny<string>()))
                .Returns(new BrowserAudioDeviceSelection("browser-mic", "browser-out", "chrome"));
            ActiveMeetLinks.Setup(value => value.GetActiveLinksAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            Coordinator.SetupGet(value => value.State).Returns(() => RecordingState);
            Coordinator.SetupGet(value => value.CanStart).Returns(() =>
                RecordingState is AppRecordingState.Idle or AppRecordingState.Completed or AppRecordingState.Error);
            Coordinator.SetupGet(value => value.CurrentSession).Returns(() => _session);
            Coordinator.Setup(value => value.StartRecordingWithDevicesAsync(
                    It.IsAny<RecordingDeviceSelection>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    RecordingState = AppRecordingState.Recording;
                    _session = new RecordingSessionInfo
                    {
                        RecordingId = Guid.NewGuid(),
                        StartedAt = Time.GetUtcNow(),
                        MicrophoneDeviceId = "mic-1",
                        OutputDeviceId = "out-1",
                        MicrophoneTempPath = "mic.tmp.wav",
                        LoopbackTempPath = "loop.tmp.wav",
                        SettingsSnapshot = RecordingSettingsSnapshot.From(_settings)
                    };
                    AutomaticStartSignal.TrySetResult();
                })
                .Returns(Task.CompletedTask);
            Coordinator.Setup(value => value.StopRecordingAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var id = _session?.RecordingId ?? Guid.Empty;
                    RecordingState = AppRecordingState.Completed;
                    return RecordingResult.Ok(id, "recording.mp3", TimeSpan.FromMinutes(1));
                });

            Service = new MeetingAutomationService(
                settingsService.Object,
                Authorization.Object,
                Calendar.Object,
                Meet.Object,
                ActiveMeetLinks.Object,
                DeviceResolver.Object,
                Coordinator.Object,
                Mock.Of<INotificationService>(),
                NullLogger<MeetingAutomationService>.Instance,
                Time);
        }

        public Mock<IGoogleAuthorizationService> Authorization { get; } = new();
        public Mock<IGoogleCalendarClient> Calendar { get; } = new();
        public Mock<IGoogleMeetClient> Meet { get; } = new();
        public Mock<IActiveMeetLinkProvider> ActiveMeetLinks { get; } = new();
        public Mock<IMeetingAudioDeviceResolver> DeviceResolver { get; } = new();
        public Mock<IRecordingCoordinator> Coordinator { get; } = new();
        public TaskCompletionSource AutomaticStartSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FakeTimeProvider Time { get; } = new(Now);
        public MeetingAutomationService Service { get; }
        public MeetingPresenceStatus Presence { get; set; } = MeetingPresenceStatus.Present;
        public Dictionary<string, MeetingPresenceStatus> PresenceByMeetingCode { get; } = [];
        public bool ThrowMeetError { get; set; }
        public AppRecordingState RecordingState { get; private set; } = AppRecordingState.Idle;

        public void CompleteRecordingExternally()
        {
            RecordingState = AppRecordingState.Completed;
            _session = null;
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _utcNow = now;
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
            _timestamp += duration.Ticks;
        }
    }
}
