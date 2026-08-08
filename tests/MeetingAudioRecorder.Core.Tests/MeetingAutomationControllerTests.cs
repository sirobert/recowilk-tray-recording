using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Core.Services;

namespace MeetingAudioRecorder.Core.Tests;

public sealed class MeetingAutomationControllerTests
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(15);

    [Fact]
    public void CalendarCandidateWithoutConfirmedPresence_DoesNotStartRecording()
    {
        var controller = new MeetingAutomationController(GracePeriod, requiredAbsenceConfirmations: 3);

        var decision = controller.Observe(Observation(
            now: TimeSpan.Zero,
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Absent));

        Assert.Equal(MeetingAutomationAction.None, decision.Action);
        Assert.Equal(MeetingAutomationState.WaitingForJoin, decision.State);
    }

    [Fact]
    public void ConfirmedPresenceWhileIdle_RequestsSingleStart()
    {
        var controller = new MeetingAutomationController(GracePeriod, requiredAbsenceConfirmations: 3);

        var first = controller.Observe(Observation(
            now: TimeSpan.Zero,
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Present));
        var duplicate = controller.Observe(Observation(
            now: TimeSpan.FromSeconds(5),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Present));

        Assert.Equal(MeetingAutomationAction.StartRecording, first.Action);
        Assert.Equal(MeetingAutomationAction.None, duplicate.Action);
        Assert.Equal(MeetingAutomationState.StartRequested, duplicate.State);
    }

    [Fact]
    public void ExistingManualRecording_IsNeverTakenOver()
    {
        var controller = new MeetingAutomationController(GracePeriod, requiredAbsenceConfirmations: 3);
        var manualRecordingId = Guid.NewGuid();

        var joined = controller.Observe(Observation(
            now: TimeSpan.Zero,
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Present,
            currentRecordingId: manualRecordingId));
        var left = controller.Observe(Observation(
            now: TimeSpan.FromSeconds(30),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Absent,
            currentRecordingId: manualRecordingId));

        Assert.Equal(MeetingAutomationAction.None, joined.Action);
        Assert.Equal(MeetingAutomationAction.None, left.Action);
        Assert.Equal(MeetingAutomationState.ManualRecordingActive, left.State);
    }

    [Fact]
    public void ConfirmedDisconnect_StopsOnlyOwnedRecordingAfterGracePeriod()
    {
        var controller = StartAutomaticRecording("calendar-event-1", out var recordingId);

        var first = controller.Observe(Observation(
            now: TimeSpan.FromSeconds(5),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Absent,
            currentRecordingId: recordingId));
        var second = controller.Observe(Observation(
            now: TimeSpan.FromSeconds(12),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Absent,
            currentRecordingId: recordingId));
        var third = controller.Observe(Observation(
            now: TimeSpan.FromSeconds(20),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Absent,
            currentRecordingId: recordingId));

        Assert.Equal(MeetingAutomationAction.None, first.Action);
        Assert.Equal(MeetingAutomationAction.None, second.Action);
        Assert.Equal(MeetingAutomationAction.StopRecording, third.Action);
        Assert.Equal(recordingId, third.OwnedRecordingId);
    }

    [Fact]
    public void UnknownPresence_DoesNotCountAsDisconnect()
    {
        var controller = StartAutomaticRecording("calendar-event-1", out var recordingId);

        controller.Observe(Observation(
            now: TimeSpan.FromSeconds(5),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Absent,
            currentRecordingId: recordingId));
        var unavailable = controller.Observe(Observation(
            now: TimeSpan.FromMinutes(2),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Unknown,
            currentRecordingId: recordingId,
            apiAvailable: false));

        Assert.Equal(MeetingAutomationAction.None, unavailable.Action);
        Assert.Equal(MeetingAutomationState.ApiUnavailable, unavailable.State);
    }

    [Fact]
    public void RejoinDuringGracePeriod_CancelsDisconnectConfirmation()
    {
        var controller = StartAutomaticRecording("calendar-event-1", out var recordingId);

        controller.Observe(Observation(
            now: TimeSpan.FromSeconds(5),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Absent,
            currentRecordingId: recordingId));
        controller.Observe(Observation(
            now: TimeSpan.FromSeconds(10),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Present,
            currentRecordingId: recordingId));
        var absentAgain = controller.Observe(Observation(
            now: TimeSpan.FromSeconds(20),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Absent,
            currentRecordingId: recordingId));

        Assert.Equal(MeetingAutomationAction.None, absentAgain.Action);
        Assert.Equal(MeetingAutomationState.ConfirmingDisconnect, absentAgain.State);
    }

    [Fact]
    public void ManualStopOfOwnedRecording_DoesNotImmediatelyRestartIt()
    {
        var controller = StartAutomaticRecording("calendar-event-1", out _);

        controller.Observe(Observation(
            now: TimeSpan.FromSeconds(5),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Present,
            currentRecordingId: null));
        var stillPresent = controller.Observe(Observation(
            now: TimeSpan.FromSeconds(10),
            meetingId: "calendar-event-1",
            presence: MeetingPresenceStatus.Present,
            currentRecordingId: null));

        Assert.Equal(MeetingAutomationAction.None, stillPresent.Action);
        Assert.Equal(MeetingAutomationState.SuppressedUntilLeave, stillPresent.State);
    }

    private static MeetingAutomationController StartAutomaticRecording(string meetingId, out Guid recordingId)
    {
        var controller = new MeetingAutomationController(GracePeriod, requiredAbsenceConfirmations: 3);
        var start = controller.Observe(Observation(
            now: TimeSpan.Zero,
            meetingId: meetingId,
            presence: MeetingPresenceStatus.Present));
        Assert.Equal(MeetingAutomationAction.StartRecording, start.Action);

        recordingId = Guid.NewGuid();
        controller.ConfirmAutomaticRecordingStarted(meetingId, recordingId);
        return controller;
    }

    private static MeetingAutomationObservation Observation(
        TimeSpan now,
        string? meetingId,
        MeetingPresenceStatus presence,
        Guid? currentRecordingId = null,
        bool enabled = true,
        bool authenticated = true,
        bool apiAvailable = true)
        => new()
        {
            MonotonicNow = now,
            Enabled = enabled,
            IsAuthenticated = authenticated,
            IsApiAvailable = apiAvailable,
            MeetingId = meetingId,
            Presence = presence,
            CurrentRecordingId = currentRecordingId,
            CanStartRecording = currentRecordingId is null
        };
}
