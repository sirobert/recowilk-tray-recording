using System.Diagnostics;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.Core.Services;

/// <summary>
/// Zarządza pełnym cyklem: start → capture → stop → mix → encode → cleanup.
/// </summary>
public sealed class RecordingCoordinator : IRecordingCoordinator
{
    private readonly ISettingsService _settingsService;
    private readonly IAudioDeviceService _deviceService;
    private readonly Func<IMicrophoneCaptureService> _micFactory;
    private readonly Func<ILoopbackCaptureService> _loopbackFactory;
    private readonly IAudioMixingService _mixingService;
    private readonly IMp3EncodingService _encodingService;
    private readonly IFileNameService _fileNameService;
    private readonly IDiskSpaceService _diskSpaceService;
    private readonly IRecordingSessionManifestStore _manifestStore;
    private readonly ILogger<RecordingCoordinator> _logger;
    private readonly RecordingStateMachine _stateMachine = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IMicrophoneCaptureService? _micCapture;
    private ILoopbackCaptureService? _loopbackCapture;
    private RecordingSessionInfo? _session;
    private CancellationTokenSource? _recordingCts;
    private Stopwatch? _durationWatch;
    private Timer? _durationTimer;
    private WaveFormatInfo? _microphoneFormat;
    private WaveFormatInfo? _loopbackFormat;
    private TimeSpan _lastDiskCheck;
    private int _diskCheckInProgress;
    private int _lowDiskStopRequested;
    private string? _lastError;
    private int _disposeState;

    public RecordingCoordinator(
        ISettingsService settingsService,
        IAudioDeviceService deviceService,
        Func<IMicrophoneCaptureService> micFactory,
        Func<ILoopbackCaptureService> loopbackFactory,
        IAudioMixingService mixingService,
        IMp3EncodingService encodingService,
        IFileNameService fileNameService,
        IDiskSpaceService diskSpaceService,
        IRecordingSessionManifestStore manifestStore,
        ILogger<RecordingCoordinator> logger)
    {
        _settingsService = settingsService;
        _deviceService = deviceService;
        _micFactory = micFactory;
        _loopbackFactory = loopbackFactory;
        _mixingService = mixingService;
        _encodingService = encodingService;
        _fileNameService = fileNameService;
        _diskSpaceService = diskSpaceService;
        _manifestStore = manifestStore;
        _logger = logger;
    }

    public event EventHandler<RecordingStateChangedEventArgs>? StateChanged;
    public event EventHandler<TimeSpan>? DurationUpdated;
    public event EventHandler<AudioLevelEventArgs>? MicrophoneLevelChanged;
    public event EventHandler<AudioLevelEventArgs>? LoopbackLevelChanged;

    public AppRecordingState State => _stateMachine.State;
    public TimeSpan CurrentDuration => _durationWatch?.Elapsed ?? TimeSpan.Zero;
    public RecordingSessionInfo? CurrentSession => _session;
    public bool CanStart => _stateMachine.CanStart && _gate.CurrentCount > 0;
    public bool CanStop => _stateMachine.CanStop;

    public async Task ToggleRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (State == AppRecordingState.Recording)
            await StopRecordingAsync(cancellationToken).ConfigureAwait(false);
        else if (_stateMachine.CanStart)
            await StartRecordingAsync(cancellationToken).ConfigureAwait(false);
        else
            _logger.LogWarning("Toggle zablokowany — stan: {State}", State);
    }

    public async Task StartRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("StartRecording zablokowany — operacja w toku.");
            throw new InvalidOperationException("Trwa inna operacja nagrywania lub zapisu. Spróbuj ponownie za chwilę.");
        }

        var enteredStarting = false;
        try
        {
            if (!_stateMachine.TryTransition(AppRecordingState.Starting, out var prev, out var err))
                throw new InvalidOperationException(err ?? "Nie można rozpocząć nagrywania.");

            enteredStarting = true;
            RaiseState(prev, AppRecordingState.Starting);

            var settings = _settingsService.Current;
            _microphoneFormat = null;
            _loopbackFormat = null;
            ValidateBeforeStart(settings);
            var settingsSnapshot = RecordingSettingsSnapshot.From(settings);

            var recordingId = Guid.NewGuid();
            AppPaths.EnsureDirectories();
            Directory.CreateDirectory(settingsSnapshot.RecordingsDirectory);

            var micTemp = Path.Combine(AppPaths.TempDirectory, $"{recordingId:N}_microphone.tmp.wav");
            var loopTemp = Path.Combine(AppPaths.TempDirectory, $"{recordingId:N}_loopback.tmp.wav");

            _session = new RecordingSessionInfo
            {
                RecordingId = recordingId,
                StartedAt = DateTimeOffset.Now,
                MicrophoneDeviceId = settingsSnapshot.MicrophoneDeviceId,
                OutputDeviceId = settingsSnapshot.OutputDeviceId,
                MicrophoneTempPath = micTemp,
                LoopbackTempPath = loopTemp,
                SettingsSnapshot = settingsSnapshot
            };
            SaveManifest(_session, state: "starting");

            _recordingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _recordingCts.Token;

            _micCapture = _micFactory();
            _loopbackCapture = _loopbackFactory();

            _micCapture.LevelChanged += OnMicLevel;
            _loopbackCapture.LevelChanged += OnLoopLevel;
            _micCapture.CaptureError += OnCaptureError;
            _loopbackCapture.CaptureError += OnCaptureError;

            // Start obu źródeł możliwie jednocześnie
            var micTask = _micCapture.StartAsync(settingsSnapshot.MicrophoneDeviceId, micTemp, token);
            var loopTask = _loopbackCapture.StartAsync(settingsSnapshot.OutputDeviceId, loopTemp, token);
            await Task.WhenAll(micTask, loopTask).ConfigureAwait(false);

            _session.MicrophoneStartOffsetTicks = _micCapture.StartOffsetTicks;
            _session.LoopbackStartOffsetTicks = _loopbackCapture.StartOffsetTicks;
            _microphoneFormat = _micCapture.CaptureFormat;
            _loopbackFormat = _loopbackCapture.CaptureFormat;
            SaveManifest(
                _session,
                state: "recording",
                microphoneFormat: _microphoneFormat,
                loopbackFormat: _loopbackFormat);

            EnsureStorageAvailable(
                settingsSnapshot,
                duration: TimeSpan.FromMinutes(2),
                sourceBytes: 0,
                includeCaptureReserve: true);

            _durationWatch = Stopwatch.StartNew();
            _lastDiskCheck = TimeSpan.Zero;
            Interlocked.Exchange(ref _lowDiskStopRequested, 0);
            _durationTimer = new Timer(_ =>
            {
                if (_durationWatch is not null)
                {
                    var elapsed = _durationWatch.Elapsed;
                    DurationUpdated?.Invoke(this, elapsed);
                    CheckStorageDuringRecording(elapsed);
                }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            if (!_stateMachine.TryTransition(AppRecordingState.Recording, out prev, out err))
                throw new InvalidOperationException(err);

            RaiseState(prev, AppRecordingState.Recording);
            _logger.LogInformation(
                "Rozpoczęto nagranie {Id}. Mic={MicId}, Out={OutId}",
                recordingId, settingsSnapshot.MicrophoneDeviceId, settingsSnapshot.OutputDeviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd startu nagrywania");
            if (enteredStarting)
            {
                await SafeAbortAsync().ConfigureAwait(false);
                _stateMachine.Force(AppRecordingState.Error);
                RaiseState(AppRecordingState.Starting, AppRecordingState.Error, ex.Message);
            }
            throw;
        }
        finally
        {
            // Gate trzymamy tylko podczas Starting; podczas Recording zwalniamy, żeby Stop mógł wejść.
            if (State == AppRecordingState.Recording)
                _gate.Release();
            else if (State is AppRecordingState.Error or AppRecordingState.Idle || !enteredStarting)
                _gate.Release();
        }
    }

    public async Task<RecordingResult> StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            // Jeśli trwa Starting — zablokuj
            if (State is AppRecordingState.Starting or AppRecordingState.Stopping or AppRecordingState.Processing)
                throw new InvalidOperationException("Trwa przetwarzanie poprzedniego nagrania. Poczekaj na zakończenie.");

            // Recording — spróbuj poczekać chwilę
            if (!await _gate.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Nie można zatrzymać nagrywania — operacja w toku.");
        }

        var session = _session;
        if (session is null)
        {
            _gate.Release();
            return RecordingResult.Fail(Guid.Empty, "Brak aktywnej sesji nagrywania.");
        }

        try
        {
            if (!_stateMachine.TryTransition(AppRecordingState.Stopping, out var prev, out var err))
            {
                // Może być już w Error po odłączeniu urządzenia
                if (State != AppRecordingState.Error && State != AppRecordingState.Recording)
                    throw new InvalidOperationException(err ?? "Nie można zatrzymać nagrywania.");
                if (State == AppRecordingState.Recording)
                    _stateMachine.Force(AppRecordingState.Stopping);
            }
            else
            {
                RaiseState(prev, AppRecordingState.Stopping);
            }

            _durationTimer?.Dispose();
            _durationTimer = null;
            _durationWatch?.Stop();
            session.StoppedAt = DateTimeOffset.Now;
            var stoppedAtMonotonicTicks = (long)(
                Stopwatch.GetTimestamp() *
                (TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency));
            session.Duration = RecordingTimeCalculator.CalculateCaptureDuration(
                stoppedAtMonotonicTicks,
                session.MicrophoneStartOffsetTicks,
                session.LoopbackStartOffsetTicks,
                _durationWatch?.Elapsed ?? TimeSpan.Zero);
            SaveManifest(session, state: "processing");

            try
            {
                if (_micCapture is not null)
                    await _micCapture.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Błąd zatrzymania mikrofonu");
            }

            try
            {
                if (_loopbackCapture is not null)
                    await _loopbackCapture.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Błąd zatrzymania loopback");
            }

            DetachCaptureHandlers();
            await DisposeCapturesAsync().ConfigureAwait(false);

            if (!_stateMachine.TryTransition(AppRecordingState.Processing, out prev, out _))
                _stateMachine.Force(AppRecordingState.Processing);
            RaiseState(prev, AppRecordingState.Processing);

            var result = await ProcessRecordingAsync(session, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                _stateMachine.Force(AppRecordingState.Completed);
                RaiseState(AppRecordingState.Processing, AppRecordingState.Completed);
                _logger.LogInformation(
                    "Zapisano nagranie {Id}, czas={Duration}, plik={Path}",
                    session.RecordingId, result.Duration, result.OutputPath);
                _manifestStore.Delete(session.RecordingId);
            }
            else
            {
                _stateMachine.Force(AppRecordingState.Error);
                RaiseState(AppRecordingState.Processing, AppRecordingState.Error, result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd zatrzymania/zapisu nagrania {Id}", session.RecordingId);
            _stateMachine.Force(AppRecordingState.Error);
            RaiseState(AppRecordingState.Stopping, AppRecordingState.Error, ex.Message);
            // NIE usuwamy plików tymczasowych
            return RecordingResult.Fail(session.RecordingId, ex.Message);
        }
        finally
        {
            _recordingCts?.Dispose();
            _recordingCts = null;
            _gate.Release();
        }
    }

    public async Task<RecordingResult> RecoverRecordingAsync(RecoverableRecording recoverable, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Trwa inna operacja. Spróbuj później.");

        try
        {
            if (!_stateMachine.CanStart)
                throw new InvalidOperationException("Nie można odzyskać nagrania w obecnym stanie.");

            _stateMachine.Force(AppRecordingState.Processing);
            RaiseState(AppRecordingState.Idle, AppRecordingState.Processing, "Odzyskiwanie nagrania…");

            var settingsSnapshot = recoverable.SettingsSnapshot
                                   ?? RecordingSettingsSnapshot.From(_settingsService.Current);
            var session = new RecordingSessionInfo
            {
                RecordingId = recoverable.RecordingId,
                StartedAt = recoverable.DetectedAt,
                MicrophoneTempPath = recoverable.MicrophoneTempPath,
                LoopbackTempPath = recoverable.LoopbackTempPath,
                MicrophoneDeviceId = recoverable.MicrophoneDeviceId,
                OutputDeviceId = recoverable.OutputDeviceId,
                SettingsSnapshot = settingsSnapshot,
                MicrophoneStartOffsetTicks = recoverable.MicrophoneStartOffsetTicks,
                LoopbackStartOffsetTicks = recoverable.LoopbackStartOffsetTicks,
                Duration = recoverable.DurationTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null
            };

            var result = await ProcessRecordingAsync(session, cancellationToken).ConfigureAwait(false);
            if (result.Success)
                _manifestStore.Delete(session.RecordingId);
            _stateMachine.Force(result.Success ? AppRecordingState.Completed : AppRecordingState.Error);
            RaiseState(AppRecordingState.Processing, result.Success ? AppRecordingState.Completed : AppRecordingState.Error, result.ErrorMessage);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RecordingResult> ProcessRecordingAsync(RecordingSessionInfo session, CancellationToken cancellationToken)
    {
        var settings = session.SettingsSnapshot;

        if (!File.Exists(session.MicrophoneTempPath) && !File.Exists(session.LoopbackTempPath))
            return RecordingResult.Fail(session.RecordingId, "Brak plików tymczasowych nagrania.");

        // Upewnij się, że brakujące pliki to cisza (pusty WAV nie — mikser obsłuży brak jednego źródła)
        Directory.CreateDirectory(settings.RecordingsDirectory);

        var sourceBytes = GetFileLength(session.MicrophoneTempPath) + GetFileLength(session.LoopbackTempPath);
        try
        {
            EnsureStorageAvailable(
                settings,
                session.Duration ?? TimeSpan.Zero,
                sourceBytes,
                includeCaptureReserve: false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Za mało miejsca na przetwarzanie nagrania");
            return RecordingResult.Fail(session.RecordingId, ex.Message);
        }

        var fileName = _fileNameService.GenerateFileName(settings.FileNameFormat, session.StartedAt);
        var finalMp3 = _fileNameService.EnsureUniquePath(settings.RecordingsDirectory, fileName);
        var mixedWav = Path.Combine(AppPaths.TempDirectory, $"{session.RecordingId:N}_mixed.tmp.wav");
        var partialMp3 = finalMp3 + ".partial";

        var additional = new List<string>();

        try
        {
            string? sepMic = null;
            string? sepLoop = null;
            if (settings.KeepSeparateTracks)
            {
                var baseName = Path.GetFileNameWithoutExtension(finalMp3);
                sepMic = Path.Combine(settings.RecordingsDirectory, baseName + "_mikrofon.wav");
                sepLoop = Path.Combine(settings.RecordingsDirectory, baseName + "_loopback.wav");
            }

            var mixRequest = new MixRequest
            {
                MicrophoneWavPath = session.MicrophoneTempPath,
                LoopbackWavPath = session.LoopbackTempPath,
                OutputWavPath = mixedWav,
                MicrophoneStartOffsetTicks = session.MicrophoneStartOffsetTicks,
                LoopbackStartOffsetTicks = session.LoopbackStartOffsetTicks,
                ExpectedDurationTicks = session.Duration?.Ticks ?? 0,
                TargetSampleRate = settings.TargetSampleRate,
                MicrophoneVolume = settings.MicrophoneVolume,
                LoopbackVolume = settings.LoopbackVolume,
                KeepSeparateTracks = settings.KeepSeparateTracks,
                SeparateMicrophoneOutputPath = sepMic,
                SeparateLoopbackOutputPath = sepLoop
            };

            await _mixingService.MixToWavAsync(mixRequest, cancellationToken).ConfigureAwait(false);

            if (settings.KeepSeparateTracks)
            {
                if (sepMic is not null && File.Exists(sepMic)) additional.Add(sepMic);
                if (sepLoop is not null && File.Exists(sepLoop)) additional.Add(sepLoop);
            }

            await _encodingService.EncodeToMp3Async(mixedWav, partialMp3, settings.Mp3BitrateKbps, cancellationToken)
                .ConfigureAwait(false);

            if (!File.Exists(partialMp3) || new FileInfo(partialMp3).Length < 128)
                return RecordingResult.Fail(session.RecordingId, "Plik MP3 nie został poprawnie utworzony.");

            // Atomowa zamiana nazwy
            if (File.Exists(finalMp3))
                File.Delete(finalMp3);
            File.Move(partialMp3, finalMp3);

            // Dopiero teraz usuń pliki tymczasowe
            TryDelete(session.MicrophoneTempPath);
            TryDelete(session.LoopbackTempPath);
            TryDelete(mixedWav);

            session.OutputMp3Path = finalMp3;
            var duration = session.Duration ?? TimeSpan.Zero;
            return RecordingResult.Ok(session.RecordingId, finalMp3, duration, additional);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Błąd przetwarzania nagrania {Id}", session.RecordingId);
            TryDelete(partialMp3);
            // Zachowaj pliki tymczasowe do odzyskania
            return RecordingResult.Fail(session.RecordingId, "Nie udało się zapisać nagrania: " + ex.Message);
        }
    }

    private void ValidateBeforeStart(AppSettings settings)
    {
        var validation = SettingsValidator.Validate(settings);
        if (!validation.IsValid)
            throw new InvalidOperationException(string.Join(" ", validation.Errors));

        var mic = _deviceService.ResolveDevice(settings.MicrophoneDeviceId, AudioDeviceType.Capture);
        if (mic.Device is null)
            throw new InvalidOperationException(mic.WarningMessage ?? "Nie znaleziono mikrofonu. Wybierz urządzenie w ustawieniach.");

        if (mic.UsedFallback)
            _logger.LogWarning("Mikrofon: {Warning}", mic.WarningMessage);

        // Zaktualizuj ID jeśli fallback
        if (mic.Device is not null && settings.MicrophoneDeviceId != mic.Device.Id)
        {
            settings.MicrophoneDeviceId = mic.Device.Id;
            _settingsService.Save(settings);
        }

        var output = _deviceService.ResolveDevice(settings.OutputDeviceId, AudioDeviceType.Render);
        if (output.Device is null)
            throw new InvalidOperationException(output.WarningMessage ?? "Nie znaleziono urządzenia wyjściowego. Wybierz je w ustawieniach.");

        if (output.UsedFallback)
            _logger.LogWarning("Wyjście: {Warning}", output.WarningMessage);

        if (output.Device is not null && settings.OutputDeviceId != output.Device.Id)
        {
            settings.OutputDeviceId = output.Device.Id;
            _settingsService.Save(settings);
        }

        Directory.CreateDirectory(settings.RecordingsDirectory);
        EnsureStorageAvailable(
            RecordingSettingsSnapshot.From(settings),
            duration: TimeSpan.FromMinutes(2),
            sourceBytes: 0,
            includeCaptureReserve: true);
    }

    private void OnMicLevel(object? sender, AudioLevelEventArgs e) => MicrophoneLevelChanged?.Invoke(this, e);
    private void OnLoopLevel(object? sender, AudioLevelEventArgs e) => LoopbackLevelChanged?.Invoke(this, e);

    private void CheckStorageDuringRecording(TimeSpan elapsed)
    {
        if (State != AppRecordingState.Recording
            || elapsed - _lastDiskCheck < TimeSpan.FromSeconds(5)
            || Interlocked.CompareExchange(ref _diskCheckInProgress, 1, 0) != 0)
        {
            return;
        }

        _lastDiskCheck = elapsed;
        try
        {
            var session = _session;
            if (session is null)
                return;

            var sourceBytes = GetFileLength(session.MicrophoneTempPath) + GetFileLength(session.LoopbackTempPath);
            EnsureStorageAvailable(
                session.SettingsSnapshot,
                elapsed,
                sourceBytes,
                includeCaptureReserve: true);
        }
        catch (InvalidOperationException ex)
        {
            if (Interlocked.Exchange(ref _lowDiskStopRequested, 1) != 0)
                return;

            var message = ex.Message + " Nagrywanie zostanie bezpiecznie zatrzymane.";
            if (_session is not null)
                _session.ErrorMessage = message;
            _logger.LogError("{Message}", message);
            RaiseState(AppRecordingState.Recording, AppRecordingState.Recording, message);

            _ = Task.Run(async () =>
            {
                try
                {
                    await StopRecordingAsync().ConfigureAwait(false);
                }
                catch (Exception stopEx)
                {
                    _logger.LogError(stopEx, "Awaryjny stop po wyczerpaniu miejsca");
                }
            });
        }
        finally
        {
            Interlocked.Exchange(ref _diskCheckInProgress, 0);
        }
    }

    private void EnsureStorageAvailable(
        RecordingSettingsSnapshot settings,
        TimeSpan duration,
        long sourceBytes,
        bool includeCaptureReserve)
    {
        var processing = StorageBudgetCalculator.CalculateProcessingBudget(
            duration,
            sourceBytes,
            settings.TargetSampleRate,
            settings.Mp3BitrateKbps,
            settings.KeepSeparateTracks);
        var captureReserve = includeCaptureReserve
            ? StorageBudgetCalculator.CalculateCaptureReserve(
                _microphoneFormat,
                _loopbackFormat,
                TimeSpan.FromMinutes(1))
            : 0;
        var tempRequired = checked(processing.TempAdditionalBytes + captureReserve);
        var outputRequired = processing.OutputAdditionalBytes;

        if (PathsShareVolume(AppPaths.TempDirectory, settings.RecordingsDirectory))
        {
            var required = checked(tempRequired + outputRequired);
            if (!_diskSpaceService.HasEnoughSpace(settings.RecordingsDirectory, required, out var available))
                throw BuildDiskSpaceException(required, available);
            return;
        }

        if (!_diskSpaceService.HasEnoughSpace(AppPaths.TempDirectory, tempRequired, out var tempAvailable))
            throw BuildDiskSpaceException(tempRequired, tempAvailable);
        if (!_diskSpaceService.HasEnoughSpace(settings.RecordingsDirectory, outputRequired, out var outputAvailable))
            throw BuildDiskSpaceException(outputRequired, outputAvailable);
    }

    private static InvalidOperationException BuildDiskSpaceException(long required, long available)
        => new(
            $"Za mało miejsca na dysku. Wymagane: {FormatBytes(required)}, dostępne: {FormatBytes(available)}.");

    private static string FormatBytes(long bytes)
        => $"{bytes / (1024.0 * 1024):F0} MB";

    private static bool PathsShareVolume(string first, string second)
    {
        try
        {
            var firstRoot = Path.GetPathRoot(Path.GetFullPath(first));
            var secondRoot = Path.GetPathRoot(Path.GetFullPath(second));
            return string.Equals(firstRoot, secondRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static long GetFileLength(string? path)
    {
        try
        {
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private void SaveManifest(
        RecordingSessionInfo session,
        string state,
        WaveFormatInfo? microphoneFormat = null,
        WaveFormatInfo? loopbackFormat = null)
    {
        var existing = _manifestStore.TryLoad(session.RecordingId);
        _manifestStore.Save(new RecordingSessionManifest
        {
            RecordingId = session.RecordingId,
            StartedAt = session.StartedAt,
            StoppedAt = session.StoppedAt,
            State = state,
            MicrophoneDeviceId = session.MicrophoneDeviceId,
            OutputDeviceId = session.OutputDeviceId,
            MicrophoneTempPath = session.MicrophoneTempPath,
            LoopbackTempPath = session.LoopbackTempPath,
            SettingsSnapshot = session.SettingsSnapshot,
            MicrophoneFormat = microphoneFormat ?? existing?.MicrophoneFormat,
            LoopbackFormat = loopbackFormat ?? existing?.LoopbackFormat,
            MicrophoneStartOffsetTicks = session.MicrophoneStartOffsetTicks,
            LoopbackStartOffsetTicks = session.LoopbackStartOffsetTicks,
            DurationTicks = session.Duration?.Ticks
        });
    }

    private void OnCaptureError(object? sender, Exception ex)
    {
        _logger.LogError(ex, "Błąd przechwytywania audio");
        _lastError = ex.Message;
        // Bezpieczne zakończenie w tle
        _ = Task.Run(async () =>
        {
            try
            {
                if (State == AppRecordingState.Recording)
                    await StopRecordingAsync().ConfigureAwait(false);
            }
            catch (Exception stopEx)
            {
                _logger.LogError(stopEx, "Błąd awaryjnego zatrzymania");
            }
        });
    }

    private async Task SafeAbortAsync()
    {
        try
        {
            _durationTimer?.Dispose();
            _durationTimer = null;
            _durationWatch?.Stop();
            DetachCaptureHandlers();
            if (_micCapture is not null)
            {
                try { await _micCapture.StopAsync().ConfigureAwait(false); } catch { /* ignore */ }
            }
            if (_loopbackCapture is not null)
            {
                try { await _loopbackCapture.StopAsync().ConfigureAwait(false); } catch { /* ignore */ }
            }
            await DisposeCapturesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SafeAbort");
        }
    }

    private void DetachCaptureHandlers()
    {
        if (_micCapture is not null)
        {
            _micCapture.LevelChanged -= OnMicLevel;
            _micCapture.CaptureError -= OnCaptureError;
        }
        if (_loopbackCapture is not null)
        {
            _loopbackCapture.LevelChanged -= OnLoopLevel;
            _loopbackCapture.CaptureError -= OnCaptureError;
        }
    }

    private async Task DisposeCapturesAsync()
    {
        if (_micCapture is not null)
        {
            await _micCapture.DisposeAsync().ConfigureAwait(false);
            _micCapture = null;
        }
        if (_loopbackCapture is not null)
        {
            await _loopbackCapture.DisposeAsync().ConfigureAwait(false);
            _loopbackCapture = null;
        }
    }

    private void RaiseState(AppRecordingState previous, AppRecordingState current, string? message = null)
        => StateChanged?.Invoke(this, new RecordingStateChangedEventArgs(previous, current, message));

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // nie usuwamy automatycznie przy błędzie — log w caller
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _durationTimer?.Dispose();
        _recordingCts?.Cancel();
        await SafeAbortAsync().ConfigureAwait(false);
        _gate.Dispose();
        _recordingCts?.Dispose();
    }
}
