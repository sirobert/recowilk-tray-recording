using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Services;

/// <summary>
/// Deterministyczna logika właściciela automatycznego nagrania. Nie wykonuje I/O
/// i używa czasu monotonicznego dostarczonego przez usługę nadrzędną.
/// </summary>
public sealed class MeetingAutomationController
{
    private readonly object _lock = new();
    private readonly TimeSpan _disconnectGracePeriod;
    private readonly int _requiredAbsenceConfirmations;

    private string? _pendingStartMeetingId;
    private string? _ownedMeetingId;
    private Guid? _ownedRecordingId;
    private string? _suppressedMeetingId;
    private TimeSpan? _absenceStartedAt;
    private int _absenceConfirmations;
    private bool _stopRequested;

    public MeetingAutomationController(
        TimeSpan disconnectGracePeriod,
        int requiredAbsenceConfirmations)
    {
        if (disconnectGracePeriod < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(disconnectGracePeriod));
        if (requiredAbsenceConfirmations < 1)
            throw new ArgumentOutOfRangeException(nameof(requiredAbsenceConfirmations));

        _disconnectGracePeriod = disconnectGracePeriod;
        _requiredAbsenceConfirmations = requiredAbsenceConfirmations;
    }

    public MeetingAutomationDecision Observe(MeetingAutomationObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        lock (_lock)
        {
            if (!observation.Enabled)
            {
                Reset();
                return Decision(MeetingAutomationAction.None, MeetingAutomationState.Disabled, observation.MeetingId);
            }

            ReconcileOwnedRecording(observation);

            if (!observation.IsAuthenticated)
                return Decision(MeetingAutomationAction.None, MeetingAutomationState.AuthenticationRequired, observation.MeetingId);

            if (!observation.IsApiAvailable)
                return Decision(MeetingAutomationAction.None, MeetingAutomationState.ApiUnavailable, observation.MeetingId);

            if (IsSuppressedWhileStillPresent(observation))
                return Decision(MeetingAutomationAction.None, MeetingAutomationState.SuppressedUntilLeave, observation.MeetingId);

            ClearSuppressionAfterLeaveOrMeetingChange(observation);

            if (_ownedRecordingId is not null)
                return ObserveOwnedRecording(observation);

            if (observation.CurrentRecordingId is not null)
            {
                _pendingStartMeetingId = null;
                return Decision(MeetingAutomationAction.None, MeetingAutomationState.ManualRecordingActive, observation.MeetingId);
            }

            if (_pendingStartMeetingId is not null)
            {
                if (observation.MeetingId == _pendingStartMeetingId
                    && observation.Presence == MeetingPresenceStatus.Present)
                {
                    return Decision(MeetingAutomationAction.None, MeetingAutomationState.StartRequested, observation.MeetingId);
                }

                _pendingStartMeetingId = null;
            }

            if (string.IsNullOrWhiteSpace(observation.MeetingId))
                return Decision(MeetingAutomationAction.None, MeetingAutomationState.WaitingForMeeting, null);

            if (observation.Presence != MeetingPresenceStatus.Present)
                return Decision(MeetingAutomationAction.None, MeetingAutomationState.WaitingForJoin, observation.MeetingId);

            if (!observation.CanStartRecording)
                return Decision(MeetingAutomationAction.None, MeetingAutomationState.WaitingForJoin, observation.MeetingId);

            _pendingStartMeetingId = observation.MeetingId;
            return Decision(MeetingAutomationAction.StartRecording, MeetingAutomationState.StartRequested, observation.MeetingId);
        }
    }

    public void ConfirmAutomaticRecordingStarted(string meetingId, Guid recordingId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meetingId);
        if (recordingId == Guid.Empty)
            throw new ArgumentException("Identyfikator nagrania nie może być pusty.", nameof(recordingId));

        lock (_lock)
        {
            if (!string.Equals(_pendingStartMeetingId, meetingId, StringComparison.Ordinal))
                throw new InvalidOperationException("Potwierdzono start dla innego spotkania niż oczekiwane.");

            _ownedMeetingId = meetingId;
            _ownedRecordingId = recordingId;
            _pendingStartMeetingId = null;
            ResetDisconnectConfirmation();
        }
    }

    public void NotifyAutomaticStartFailed(string meetingId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meetingId);

        lock (_lock)
        {
            if (string.Equals(_pendingStartMeetingId, meetingId, StringComparison.Ordinal))
                _pendingStartMeetingId = null;
        }
    }

    public void NotifyAutomaticStopFailed(Guid recordingId)
    {
        if (recordingId == Guid.Empty)
            throw new ArgumentException("Identyfikator nagrania nie może być pusty.", nameof(recordingId));

        lock (_lock)
        {
            if (_ownedRecordingId == recordingId)
                ResetDisconnectConfirmation();
        }
    }

    private MeetingAutomationDecision ObserveOwnedRecording(MeetingAutomationObservation observation)
    {
        if (!string.Equals(observation.MeetingId, _ownedMeetingId, StringComparison.Ordinal))
            return Decision(MeetingAutomationAction.None, MeetingAutomationState.ApiUnavailable, _ownedMeetingId);

        if (observation.Presence == MeetingPresenceStatus.Present)
        {
            ResetDisconnectConfirmation();
            return Decision(MeetingAutomationAction.None, MeetingAutomationState.RecordingAutomatically, observation.MeetingId);
        }

        if (observation.Presence == MeetingPresenceStatus.Unknown)
            return Decision(MeetingAutomationAction.None, MeetingAutomationState.ApiUnavailable, observation.MeetingId);

        _absenceStartedAt ??= observation.MonotonicNow;
        _absenceConfirmations++;

        var graceElapsed = observation.MonotonicNow - _absenceStartedAt.Value;
        if (!_stopRequested
            && _absenceConfirmations >= _requiredAbsenceConfirmations
            && graceElapsed >= _disconnectGracePeriod)
        {
            _stopRequested = true;
            return Decision(MeetingAutomationAction.StopRecording, MeetingAutomationState.ConfirmingDisconnect, observation.MeetingId);
        }

        return Decision(MeetingAutomationAction.None, MeetingAutomationState.ConfirmingDisconnect, observation.MeetingId);
    }

    private void ReconcileOwnedRecording(MeetingAutomationObservation observation)
    {
        if (_ownedRecordingId is null || observation.CurrentRecordingId == _ownedRecordingId)
            return;

        _suppressedMeetingId = _ownedMeetingId;
        _ownedMeetingId = null;
        _ownedRecordingId = null;
        _pendingStartMeetingId = null;
        ResetDisconnectConfirmation();
    }

    private bool IsSuppressedWhileStillPresent(MeetingAutomationObservation observation)
        => _suppressedMeetingId is not null
           && string.Equals(_suppressedMeetingId, observation.MeetingId, StringComparison.Ordinal)
           && observation.Presence is MeetingPresenceStatus.Present or MeetingPresenceStatus.Unknown;

    private void ClearSuppressionAfterLeaveOrMeetingChange(MeetingAutomationObservation observation)
    {
        if (_suppressedMeetingId is null)
            return;

        if (!string.Equals(_suppressedMeetingId, observation.MeetingId, StringComparison.Ordinal)
            || observation.Presence is MeetingPresenceStatus.Absent or MeetingPresenceStatus.ConferenceEnded)
        {
            _suppressedMeetingId = null;
        }
    }

    private MeetingAutomationDecision Decision(
        MeetingAutomationAction action,
        MeetingAutomationState state,
        string? meetingId)
        => new(action, state, meetingId, _ownedRecordingId);

    private void ResetDisconnectConfirmation()
    {
        _absenceStartedAt = null;
        _absenceConfirmations = 0;
        _stopRequested = false;
    }

    private void Reset()
    {
        _pendingStartMeetingId = null;
        _ownedMeetingId = null;
        _ownedRecordingId = null;
        _suppressedMeetingId = null;
        ResetDisconnectConfirmation();
    }
}
