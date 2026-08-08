using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.Core.Services;

public sealed class MeetingAutomationService : IMeetingAutomationService
{
    private static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CalendarPastWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan CalendarLookAhead = TimeSpan.FromMinutes(15);

    private readonly ISettingsService _settingsService;
    private readonly IGoogleAuthorizationService _authorizationService;
    private readonly IGoogleCalendarClient _calendarClient;
    private readonly IGoogleMeetClient _meetClient;
    private readonly IActiveMeetLinkProvider _activeMeetLinkProvider;
    private readonly IMeetingAudioDeviceResolver _audioDeviceResolver;
    private readonly IRecordingCoordinator _recordingCoordinator;
    private readonly INotificationService _notificationService;
    private readonly ILogger<MeetingAutomationService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly MeetingAutomationController _controller = new(DisconnectGracePeriod, 3);
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly object _lifecycleLock = new();
    private readonly object _statusLock = new();
    private readonly long _startedTimestamp;

    private CancellationTokenSource? _loopCancellation;
    private Task? _loopTask;
    private GoogleCalendarMeeting? _trackedMeeting;
    private MeetingAutomationStatus _status = new(
        MeetingAutomationState.Disabled,
        "Automatyczne nagrywanie Google Meet jest wyłączone.");

    public MeetingAutomationService(
        ISettingsService settingsService,
        IGoogleAuthorizationService authorizationService,
        IGoogleCalendarClient calendarClient,
        IGoogleMeetClient meetClient,
        IActiveMeetLinkProvider activeMeetLinkProvider,
        IMeetingAudioDeviceResolver audioDeviceResolver,
        IRecordingCoordinator recordingCoordinator,
        INotificationService notificationService,
        ILogger<MeetingAutomationService> logger)
        : this(
            settingsService,
            authorizationService,
            calendarClient,
            meetClient,
            activeMeetLinkProvider,
            audioDeviceResolver,
            recordingCoordinator,
            notificationService,
            logger,
            TimeProvider.System)
    {
    }

    public MeetingAutomationService(
        ISettingsService settingsService,
        IGoogleAuthorizationService authorizationService,
        IGoogleCalendarClient calendarClient,
        IGoogleMeetClient meetClient,
        IActiveMeetLinkProvider activeMeetLinkProvider,
        IMeetingAudioDeviceResolver audioDeviceResolver,
        IRecordingCoordinator recordingCoordinator,
        INotificationService notificationService,
        ILogger<MeetingAutomationService> logger,
        TimeProvider timeProvider)
    {
        _settingsService = settingsService;
        _authorizationService = authorizationService;
        _calendarClient = calendarClient;
        _meetClient = meetClient;
        _activeMeetLinkProvider = activeMeetLinkProvider;
        _audioDeviceResolver = audioDeviceResolver;
        _recordingCoordinator = recordingCoordinator;
        _notificationService = notificationService;
        _logger = logger;
        _timeProvider = timeProvider;
        _startedTimestamp = timeProvider.GetTimestamp();
        _activeMeetLinkProvider.ActiveLinksChanged += OnActiveMeetLinksChanged;
    }

    public event EventHandler<MeetingAutomationStatus>? StatusChanged;

    public MeetingAutomationStatus Status
    {
        get
        {
            lock (_statusLock)
                return _status;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lifecycleLock)
        {
            if (_loopTask is not null)
                return Task.CompletedTask;

            _loopCancellation = new CancellationTokenSource();
            _loopTask = RunLoopAsync(_loopCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? loopTask;
        lock (_lifecycleLock)
        {
            loopTask = _loopTask;
            _loopCancellation?.Cancel();
        }

        if (loopTask is not null)
        {
            try
            {
                await loopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Oczekiwane anulowanie własnej pętli.
            }
        }

        lock (_lifecycleLock)
        {
            _loopCancellation?.Dispose();
            _loopCancellation = null;
            _loopTask = null;
        }
    }

    public async Task CheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            await CheckCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _checkGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _activeMeetLinkProvider.ActiveLinksChanged -= OnActiveMeetLinksChanged;
        await StopAsync().ConfigureAwait(false);
        _checkGate.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CheckNowAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nieobsłużony błąd pętli automatyki Google Meet");
                Publish(new MeetingAutomationStatus(
                    MeetingAutomationState.ApiUnavailable,
                    "Automatyka Google Meet napotkała błąd i spróbuje ponownie."));
            }

            var active = _trackedMeeting is not null
                         || _recordingCoordinator.State == AppRecordingState.Recording;
            var interval = active ? ActivePollInterval : IdlePollInterval;
            try
            {
                await Task.Delay(interval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CheckCoreAsync(CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current;
        if (!settings.GoogleMeetAutomationEnabled)
        {
            _trackedMeeting = null;
            var decision = _controller.Observe(CreateObservation(
                enabled: false,
                authenticated: false,
                apiAvailable: false,
                meetingId: null,
                presence: MeetingPresenceStatus.Unknown));
            PublishDecision(decision, null);
            return;
        }

        GoogleConnectionInfo connection;
        try
        {
            connection = await _authorizationService.GetConnectionInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Nie można odczytać połączenia Google");
            PublishApiUnavailable(isAuthenticated: false);
            return;
        }

        if (!connection.IsConnected || string.IsNullOrWhiteSpace(connection.AccountUserId))
        {
            var decision = _controller.Observe(CreateObservation(
                enabled: true,
                authenticated: false,
                apiAvailable: true,
                meetingId: _trackedMeeting?.EventId,
                presence: MeetingPresenceStatus.Unknown));
            PublishDecision(decision, _trackedMeeting);
            return;
        }

        try
        {
            var (meeting, presence) = await ResolvePresenceAsync(
                connection.AccountUserId,
                cancellationToken).ConfigureAwait(false);
            if (presence == MeetingPresenceStatus.Present && meeting is not null)
                _trackedMeeting = meeting;

            var decision = _controller.Observe(CreateObservation(
                enabled: true,
                authenticated: true,
                apiAvailable: true,
                meetingId: meeting?.EventId,
                presence: presence));
            await ApplyDecisionAsync(decision, meeting, cancellationToken).ConfigureAwait(false);
        }
        catch (GoogleAuthenticationRequiredException ex)
        {
            _logger.LogWarning(ex, "Połączenie Google wymaga ponownej autoryzacji");
            var decision = _controller.Observe(CreateObservation(
                enabled: true,
                authenticated: false,
                apiAvailable: true,
                meetingId: _trackedMeeting?.EventId,
                presence: MeetingPresenceStatus.Unknown));
            PublishDecision(decision, _trackedMeeting);
        }
        catch (Exception ex) when (IsTransientApiFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Tymczasowy błąd Google Calendar/Meet");
            PublishApiUnavailable(isAuthenticated: true);
        }
    }

    private async Task<(GoogleCalendarMeeting? Meeting, MeetingPresenceStatus Presence)> ResolvePresenceAsync(
        string accountUserId,
        CancellationToken cancellationToken)
    {
        if (_trackedMeeting is not null)
        {
            var trackedPresence = await _meetClient.GetCurrentUserPresenceAsync(
                _trackedMeeting.MeetingCode,
                accountUserId,
                cancellationToken).ConfigureAwait(false);
            return (_trackedMeeting, trackedPresence.Status);
        }

        var now = _timeProvider.GetUtcNow();
        var activeLinks = await _activeMeetLinkProvider.GetActiveLinksAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<GoogleCalendarMeeting> meetings;
        try
        {
            meetings = await _calendarClient.ListMeetingCandidatesAsync(
                now - CalendarPastWindow,
                now + CalendarLookAhead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (activeLinks.Count > 0 && IsTransientApiFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Calendar niedostępny; sprawdzanie aktywnego linku Meet jest kontynuowane");
            meetings = [];
        }

        var browserMeetings = activeLinks.Select(link => new GoogleCalendarMeeting(
            $"browser:{link.MeetingCode}",
            $"Google Meet {link.MeetingCode}",
            now - CalendarPastWindow,
            now.AddHours(24),
            $"https://meet.google.com/{link.MeetingCode}",
            link.MeetingCode));
        var candidates = meetings
            .Where(meeting => meeting.StartsAt <= now + CalendarLookAhead
                              && meeting.EndsAt >= now - CalendarPastWindow)
            .Concat(browserMeetings)
            .DistinctBy(meeting => meeting.MeetingCode, StringComparer.OrdinalIgnoreCase)
            .OrderBy(meeting => meeting.StartsAt)
            .Take(10)
            .ToArray();

        GoogleCalendarMeeting? firstReachable = null;
        Exception? lastFailure = null;
        foreach (var candidate in candidates)
        {
            try
            {
                var result = await _meetClient.GetCurrentUserPresenceAsync(
                    candidate.MeetingCode,
                    accountUserId,
                    cancellationToken).ConfigureAwait(false);
                firstReachable ??= candidate;
                if (result.Status == MeetingPresenceStatus.Present)
                    return (candidate, result.Status);
            }
            catch (Exception ex) when (IsTransientApiFailure(ex, cancellationToken))
            {
                lastFailure = ex;
            }
        }

        if (firstReachable is not null)
            return (firstReachable, MeetingPresenceStatus.Absent);
        if (lastFailure is not null)
            throw lastFailure;
        return (null, MeetingPresenceStatus.Unknown);
    }

    private async Task ApplyDecisionAsync(
        MeetingAutomationDecision decision,
        GoogleCalendarMeeting? meeting,
        CancellationToken cancellationToken)
    {
        if (decision.Action == MeetingAutomationAction.StartRecording && meeting is not null)
        {
            try
            {
                var settings = _settingsService.Current;
                var detected = _audioDeviceResolver.DetectActiveBrowserDevices(
                    settings.MicrophoneDeviceId,
                    settings.OutputDeviceId);
                var selection = new RecordingDeviceSelection(
                    detected.MicrophoneDeviceId,
                    detected.OutputDeviceId,
                    BuildDeviceSelectionReason(detected));
                await _recordingCoordinator.StartRecordingWithDevicesAsync(selection, cancellationToken)
                    .ConfigureAwait(false);
                var session = _recordingCoordinator.CurrentSession
                              ?? throw new InvalidOperationException("Koordynator nie udostępnił rozpoczętej sesji.");
                _controller.ConfirmAutomaticRecordingStarted(meeting.EventId, session.RecordingId);
                _trackedMeeting = meeting;
                _notificationService.ShowInfo(
                    "Google Meet",
                    $"Automatycznie rozpoczęto nagrywanie: {meeting.Title}. {BuildDeviceSummary(detected)}");
                Publish(new MeetingAutomationStatus(
                    MeetingAutomationState.RecordingAutomatically,
                    "Nagrywanie uruchomione automatycznie.",
                    meeting.Title));
            }
            catch (Exception ex)
            {
                _controller.NotifyAutomaticStartFailed(meeting.EventId);
                _logger.LogWarning(ex, "Nie udało się automatycznie rozpocząć nagrywania");
                _notificationService.ShowError("Google Meet", "Nie udało się automatycznie rozpocząć nagrywania.");
                Publish(new MeetingAutomationStatus(
                    MeetingAutomationState.WaitingForJoin,
                    "Nie udało się rozpocząć nagrywania; automatyka spróbuje ponownie.",
                    meeting.Title));
            }

            return;
        }

        if (decision.Action == MeetingAutomationAction.StopRecording
            && decision.OwnedRecordingId is { } ownedRecordingId)
        {
            try
            {
                var result = await _recordingCoordinator.StopRecordingAsync(cancellationToken).ConfigureAwait(false);
                _trackedMeeting = null;
                if (result.Success)
                {
                    _notificationService.ShowSuccess(
                        "Google Meet",
                        "Spotkanie zakończone. Automatyczne nagranie zostało zapisane.",
                        result.OutputPath);
                }
                else
                {
                    _notificationService.ShowError(
                        "Google Meet",
                        "Spotkanie zakończone, ale zapis nagrania wymaga uwagi. Pliki tymczasowe zachowano.");
                }

                Publish(new MeetingAutomationStatus(
                    result.Success ? MeetingAutomationState.WaitingForMeeting : MeetingAutomationState.ApiUnavailable,
                    result.Success ? "Automatyczne nagranie zapisane." : "Nie udało się zapisać automatycznego nagrania."));
            }
            catch (Exception ex)
            {
                _controller.NotifyAutomaticStopFailed(ownedRecordingId);
                _logger.LogWarning(ex, "Nie udało się automatycznie zatrzymać nagrywania");
                Publish(new MeetingAutomationStatus(
                    MeetingAutomationState.ConfirmingDisconnect,
                    "Nie udało się zatrzymać nagrania; automatyka spróbuje ponownie.",
                    meeting?.Title));
            }

            return;
        }

        PublishDecision(decision, meeting);
    }

    private MeetingAutomationObservation CreateObservation(
        bool enabled,
        bool authenticated,
        bool apiAvailable,
        string? meetingId,
        MeetingPresenceStatus presence)
        => new()
        {
            MonotonicNow = _timeProvider.GetElapsedTime(_startedTimestamp),
            Enabled = enabled,
            IsAuthenticated = authenticated,
            IsApiAvailable = apiAvailable,
            MeetingId = meetingId,
            Presence = presence,
            CurrentRecordingId = GetActiveRecordingId(),
            CanStartRecording = _recordingCoordinator.CanStart
        };

    private Guid? GetActiveRecordingId()
        => _recordingCoordinator.State is AppRecordingState.Starting
            or AppRecordingState.Recording
            or AppRecordingState.Stopping
            or AppRecordingState.Processing
            ? _recordingCoordinator.CurrentSession?.RecordingId
            : null;

    private void PublishApiUnavailable(bool isAuthenticated)
    {
        var decision = _controller.Observe(CreateObservation(
            enabled: true,
            authenticated: isAuthenticated,
            apiAvailable: false,
            meetingId: _trackedMeeting?.EventId,
            presence: MeetingPresenceStatus.Unknown));
        PublishDecision(decision, _trackedMeeting);
    }

    private void PublishDecision(
        MeetingAutomationDecision decision,
        GoogleCalendarMeeting? meeting)
    {
        var message = decision.State switch
        {
            MeetingAutomationState.Disabled => "Automatyczne nagrywanie Google Meet jest wyłączone.",
            MeetingAutomationState.WaitingForMeeting => "Oczekiwanie na spotkanie Google Meet.",
            MeetingAutomationState.WaitingForJoin => "Wydarzenie znalezione; oczekiwanie na Twoje dołączenie.",
            MeetingAutomationState.StartRequested => "Uruchamianie automatycznego nagrywania.",
            MeetingAutomationState.RecordingAutomatically => "Nagrywanie uruchomione automatycznie.",
            MeetingAutomationState.ConfirmingDisconnect => "Potwierdzanie rozłączenia ze spotkaniem.",
            MeetingAutomationState.ManualRecordingActive => "Trwa nagranie uruchomione ręcznie; automatyka go nie zatrzyma.",
            MeetingAutomationState.SuppressedUntilLeave => "Automatyczny start wstrzymany do opuszczenia spotkania.",
            MeetingAutomationState.AuthenticationRequired => "Połącz konto Google w ustawieniach.",
            MeetingAutomationState.ApiUnavailable => "Google jest chwilowo niedostępne; aktywne nagranie pozostaje bez zmian.",
            _ => decision.State.ToString()
        };
        Publish(new MeetingAutomationStatus(decision.State, message, meeting?.Title));
    }

    private void Publish(MeetingAutomationStatus status)
    {
        lock (_statusLock)
            _status = status;
        StatusChanged?.Invoke(this, status);
    }

    private static bool IsTransientApiFailure(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException
           or InvalidDataException
           or System.Text.Json.JsonException
           || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    private static string BuildDeviceSelectionReason(BrowserAudioDeviceSelection selection)
        => selection.BrowserProcessName is null
            ? "Zapisane urządzenia audio"
            : $"Aktywne sesje audio procesu {selection.BrowserProcessName}";

    private static string BuildDeviceSummary(BrowserAudioDeviceSelection selection)
    {
        if (!selection.HasDetectedDevice)
            return "Użyto urządzeń zapisanych w ustawieniach.";

        var microphone = selection.MicrophoneFriendlyName ?? "mikrofon z ustawień";
        var output = selection.OutputFriendlyName ?? "wyjście z ustawień";
        return $"Wykryto {selection.BrowserProcessName}: mikrofon „{microphone}”, wyjście „{output}”.";
    }

    private void OnActiveMeetLinksChanged(object? sender, EventArgs e)
        => _ = CheckAfterBrowserSignalAsync();

    private async Task CheckAfterBrowserSignalAsync()
    {
        try
        {
            await CheckNowAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się sprawdzić sygnału rozszerzenia Meet");
        }
    }
}
