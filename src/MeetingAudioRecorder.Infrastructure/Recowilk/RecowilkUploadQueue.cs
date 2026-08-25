using System.Buffers;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using Microsoft.Extensions.Logging;

namespace MeetingAudioRecorder.Infrastructure.Recowilk;

public sealed class RecowilkUploadQueue : IRecowilkUploadQueue
{
    private const int SchemaVersion = 2;
    private const int DefaultChunkSize = 5 * 1024 * 1024;
    private const int MinChunkSize = 256 * 1024;
    private const int MaxChunkSize = 16 * 1024 * 1024;
    private const long MaxFileSize = 4L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ISettingsService _settings;
    private readonly IRecowilkCredentialStore _credentials;
    private readonly ILogger<RecowilkUploadQueue> _logger;
    private readonly RecowilkApiClient _api;
    private readonly string _queueDirectory;
    private readonly IRecowilkQueueProtector _protector;
    private readonly IRecordingCatalog _catalog;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly SemaphoreSlim _processGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private int _started;
    private int _disposed;
    private int _reactivateWaiting;

    public RecowilkUploadQueue(IHttpClientFactory httpClientFactory, ISettingsService settings,
        IRecowilkCredentialStore credentials, ILogger<RecowilkUploadQueue> logger)
        : this(httpClientFactory, settings, credentials, logger, AppPaths.RecowilkUploadsDirectory,
            new DpapiRecowilkQueueProtector(), NullRecordingCatalog.Instance)
    { }

    public RecowilkUploadQueue(IHttpClientFactory httpClientFactory, ISettingsService settings,
        IRecowilkCredentialStore credentials, ILogger<RecowilkUploadQueue> logger, IRecordingCatalog catalog)
        : this(httpClientFactory, settings, credentials, logger, AppPaths.RecowilkUploadsDirectory,
            new DpapiRecowilkQueueProtector(), catalog)
    { }

    internal RecowilkUploadQueue(IHttpClientFactory httpClientFactory, ISettingsService settings,
        IRecowilkCredentialStore credentials, ILogger<RecowilkUploadQueue> logger,
        string queueDirectory, IRecowilkQueueProtector protector, IRecordingCatalog? catalog = null)
    {
        _settings = settings;
        _credentials = credentials;
        _logger = logger;
        _api = new RecowilkApiClient(httpClientFactory);
        _queueDirectory = queueDirectory;
        _protector = protector;
        _catalog = catalog ?? NullRecordingCatalog.Instance;
        _settings.SettingsChanged += OnSettingsChanged;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) == 0)
            _worker = Task.Run(() => RunAsync(_stop.Token));
        Wake();
    }

    public void Enqueue(RecordingCompletedEventArgs completed)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var settings = _settings.Current;
        if (completed.Result.OutputPath is null) return;
        var enabled = settings.RecowilkUploadEnabled && _credentials.HasKey;
        PendingUploadState? item = null;
        if (enabled)
        {
            item = PendingUploadState.From(completed);
            Save(item);
        }
        TryCatalog(() => _catalog.Upsert(RecordingCatalogEntry.FromCompleted(completed,
            enabled ? RecordingExportStatus.Queued : RecordingExportStatus.LocalOnly)), completed.Result.RecordingId);
        if (item is null) return;
        _logger.LogInformation("Dodano nagranie {RecordingId} do kolejki RecoWilk", item.RecordingId);
        Wake();
    }

    public RecordingRetryResult RetryExport(Guid recordingId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var entry = _catalog.Get(recordingId);
        if (entry is null) return RecordingRetryResult.Failed("Nie znaleziono nagrania w lokalnym katalogu.");
        if (entry.ExportStatus == RecordingExportStatus.Exported)
            return RecordingRetryResult.Failed("Nagranie zostało już wysłane do RecoWilk.");
        if (!File.Exists(entry.AudioPath))
        {
            entry.ExportStatus = RecordingExportStatus.MissingFile;
            _catalog.Upsert(entry);
            return RecordingRetryResult.Failed("Lokalny plik MP3 nie istnieje.");
        }
        var settings = _settings.Current;
        if (!settings.RecowilkUploadEnabled || !_credentials.HasKey || !TryBaseUri(settings.RecowilkBaseUrl, out _))
            return RecordingRetryResult.Failed("Włącz eksport RecoWilk i zapisz poprawny URL oraz klucz API.");
        Directory.CreateDirectory(_queueDirectory);
        var path = QueuePath(recordingId);
        PendingUploadState item;
        try { item = File.Exists(path) ? Load(path) : PendingUploadState.From(entry); }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or InvalidDataException or FormatException)
        { if (File.Exists(path)) Quarantine(path); item = PendingUploadState.From(entry); }
        item.Stage = item.MeetingId is null ? UploadStage.CreatingMeeting
            : item.UploadId is null ? UploadStage.InitializingUpload : UploadStage.UploadingChunks;
        item.Attempts = 0;
        item.NextAttemptAt = DateTimeOffset.UtcNow;
        item.LastErrorCategory = null;
        item.LastHttpStatusCode = null;
        item.LastTraceId = null;
        Save(item);
        UpdateCatalog(item, RecordingExportStatus.Queued);
        Wake();
        return RecordingRetryResult.Ok();
    }

    public async Task<RecowilkConnectionResult> TestConnectionAsync(string baseUrl, string? candidateKey,
        CancellationToken cancellationToken = default)
    {
        var key = string.IsNullOrWhiteSpace(candidateKey) ? _credentials.Load() : candidateKey.Trim();
        if (key is null || !TryBaseUri(baseUrl, out var baseUri))
            return RecowilkConnectionResult.Invalid(RecowilkConnectionFailure.InvalidConfiguration);
        return await _api.PingAsync(baseUri, key, cancellationToken).ConfigureAwait(false);
    }

    internal async Task ProcessPendingOnceAsync(CancellationToken cancellationToken = default)
    {
        await _processGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_queueDirectory);
            RecoverTemporaryFiles();
            MigrateLegacyFiles();
            ImportPendingCatalogEntries();
            var settings = _settings.Current;
            var key = _credentials.Load();
            if (!settings.RecowilkUploadEnabled || key is null || !TryBaseUri(settings.RecowilkBaseUrl, out var baseUri)) return;
            if (Interlocked.Exchange(ref _reactivateWaiting, 0) != 0)
                ReactivateWaitingItems();
            foreach (var path in Directory.EnumerateFiles(_queueDirectory, "*.upload").Order(StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                PendingUploadState item;
                try { item = Load(path); }
                catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or InvalidDataException or FormatException)
                {
                    Quarantine(path);
                    _logger.LogWarning("Uszkodzony wpis kolejki RecoWilk został przeniesiony do kwarantanny");
                    continue;
                }
                EnsureCatalog(item);
                if (item.NextAttemptAt > DateTimeOffset.UtcNow) continue;
                try
                {
                    ValidateLocalFile(item.AudioPath);
                    item.LastAttemptAt = DateTimeOffset.UtcNow;
                    UpdateCatalog(item, RecordingExportStatus.Connecting);
                    await ProcessItemAsync(item, baseUri, key, cancellationToken).ConfigureAwait(false);
                    if (!UpdateCatalog(item, RecordingExportStatus.Exported, exported: true))
                        throw new IOException("Nie udało się utrwalić potwierdzenia eksportu w katalogu nagrań.");
                    File.Delete(path);
                    _logger.LogInformation("Wysłano nagranie {RecordingId} do RecoWilk", item.RecordingId);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    ApplyFailure(item, ex);
                    Save(item);
                    UpdateCatalogFailure(item);
                    _logger.LogWarning("Upload {RecordingId} zatrzymany na etapie {Stage}; kategoria {Category}; HTTP {Status}; trace {TraceId}; próba {Attempt}",
                        item.RecordingId, item.Stage, item.LastErrorCategory,
                        ex is RecowilkApiException apiError ? (int)apiError.StatusCode : null,
                        ex is RecowilkApiException apiTrace ? apiTrace.TraceId : null, item.Attempts);
                }
            }
        }
        finally { _processGate.Release(); }
    }

    private async Task ProcessItemAsync(PendingUploadState item, Uri baseUri, string key, CancellationToken ct)
    {
        var connection = await _api.PingAsync(baseUri, key, ct).ConfigureAwait(false);
        if (!connection.Success)
        {
            if (connection.Failure is RecowilkConnectionFailure.InvalidConfiguration or RecowilkConnectionFailure.InvalidResponse)
                throw new InvalidDataException("Niepoprawna konfiguracja lub odpowiedź ping RecoWilk.");
            throw ConnectionException(connection);
        }
        BindTarget(item, baseUri, connection);
        Save(item);
        if (item.MeetingId is null)
        {
            item.Stage = UploadStage.CreatingMeeting;
            Save(item);
            UpdateCatalog(item, RecordingExportStatus.CreatingMeeting);
            var source = item.Source;
            var externalId = $"recorder:{item.RecordingId:D}";
            var result = await _api.CreateMeetingAsync(baseUri, key, new
            {
                externalId,
                title = NormalizeTitle(source?.Title, item.AudioPath),
                description = Truncate(source?.Description, 10_000),
                scheduledAt = source?.ScheduledAt,
                startedAt = item.StartedAt,
                endedAt = item.StoppedAt,
                source = new { provider = source?.Provider ?? "ManualRecorder", client = "MeetingAudioRecorder", externalEventId = source?.ExternalEventId, meetingUrl = source?.MeetingUrl },
                participants = source?.Participants.Select(p => new { displayName = p.DisplayName, fullName = p.DisplayName, email = p.Email, role = p.IsOrganizer ? "Organizer" : "Attendee" }),
                consentConfirmed = false
            }, externalId, ct).ConfigureAwait(false);
            if (result.MeetingId == Guid.Empty) throw new InvalidDataException("Brak meetingId w odpowiedzi RecoWilk.");
            item.MeetingId = result.MeetingId;
            Save(item);
        }
        for (var sessionAttempt = 0; sessionAttempt < 3; sessionAttempt++)
        {
            try
            {
                if (item.UploadId is null) await InitializeUploadAsync(item, baseUri, key, ct).ConfigureAwait(false);
                await UploadChunksAndCompleteAsync(item, baseUri, key, ct).ConfigureAwait(false);
                item.Stage = UploadStage.Completed;
                return;
            }
            catch (RecowilkApiException ex) when (ex.StatusCode == HttpStatusCode.Gone || CodeIs(ex, "upload_expired"))
            {
                ResetUpload(item, true);
                Save(item);
            }
        }
        throw new InvalidDataException("RecoWilk wielokrotnie zwrócił wygasłą sesję uploadu.");
    }

    private async Task InitializeUploadAsync(PendingUploadState item, Uri baseUri, string key, CancellationToken ct)
    {
        item.Stage = UploadStage.InitializingUpload;
        Save(item);
        UpdateCatalog(item, RecordingExportStatus.InitializingUpload);
        var info = new FileInfo(item.AudioPath);
        var result = await _api.InitUploadAsync(baseUri, key, item.MeetingId!.Value, new
        {
            fileName = info.Name,
            sizeBytes = info.Length,
            chunkSizeBytes = DefaultChunkSize,
            codec = "mp3",
            durationMs = item.DurationMs
        }, $"recorder:{item.RecordingId:D}:audio:{item.UploadSessionNumber}", ct).ConfigureAwait(false);
        ValidateUploadGeometry(info.Length, result.UploadId, result.ChunkSize, result.TotalChunks);
        item.UploadId = result.UploadId;
        item.ChunkSize = result.ChunkSize;
        item.TotalChunks = result.TotalChunks;
        item.ExpiresAt = result.ExpiresAt;
        item.Stage = UploadStage.UploadingChunks;
        Save(item);
        UpdateCatalog(item, RecordingExportStatus.Uploading);
    }

    private async Task UploadChunksAndCompleteAsync(PendingUploadState item, Uri baseUri, string key, CancellationToken ct)
    {
        for (var round = 0; round < 8; round++)
        {
            item.Stage = UploadStage.UploadingChunks;
            var status = await _api.GetStatusAsync(baseUri, key, item.UploadId!.Value, ct).ConfigureAwait(false);
            var missing = ValidateStatus(item, status);
            item.UploadedChunks = item.TotalChunks - missing.Length;
            UpdateCatalog(item, RecordingExportStatus.Uploading);
            var refresh = false;
            await using var stream = new FileStream(item.AudioPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                Math.Min(item.ChunkSize, DefaultChunkSize), FileOptions.Asynchronous | FileOptions.SequentialScan);
            foreach (var index in missing)
            {
                ct.ThrowIfCancellationRequested();
                var offset = checked((long)index * item.ChunkSize);
                var remaining = checked(stream.Length - offset);
                var length = checked((int)Math.Min(item.ChunkSize, remaining));
                if (length <= 0) throw new InvalidDataException("Niepoprawny zakres fragmentu RecoWilk.");
                var rented = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    stream.Position = offset;
                    await stream.ReadExactlyAsync(rented.AsMemory(0, length), ct).ConfigureAwait(false);
                    var chunk = rented.AsSpan(0, length).ToArray();
                    var hash = Convert.ToHexString(SHA256.HashData(chunk)).ToLowerInvariant();
                    try { await _api.PutChunkAsync(baseUri, key, item.UploadId.Value, index, chunk, hash, ct).ConfigureAwait(false); }
                    catch (RecowilkApiException ex) when (IsChunkRefresh(ex)) { refresh = true; break; }
                    item.UploadedChunks++;
                    UpdateCatalog(item, RecordingExportStatus.Uploading);
                }
                finally { ArrayPool<byte>.Shared.Return(rented, true); }
            }
            if (refresh) continue;
            item.Stage = UploadStage.Completing;
            Save(item);
            UpdateCatalog(item, RecordingExportStatus.Completing);
            try
            {
                var completed = await _api.CompleteAsync(baseUri, key, item.UploadId.Value, ct).ConfigureAwait(false);
                item.AudioAssetId = completed.AudioAssetId;
                item.ProcessingJobId = completed.ProcessingJobId;
                return;
            }
            catch (RecowilkApiException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity && CodeIs(ex, "upload_incomplete")) { }
        }
        throw new InvalidDataException("Nie udało się uzyskać kompletnego statusu uploadu RecoWilk.");
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingOnceAsync(ct).ConfigureAwait(false);
                await _signal.WaitAsync(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Błąd pętli kolejki RecoWilk");
                await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            }
        }
    }

    private void BindTarget(PendingUploadState item, Uri baseUri, RecowilkConnectionResult connection)
    {
        var canonical = CanonicalBaseUrl(baseUri);
        var changed = item.BaseUrl is not null && (!string.Equals(item.BaseUrl, canonical, StringComparison.OrdinalIgnoreCase)
            || item.OrganizationId != connection.OrganizationId || item.MeetingOwnerId != connection.MeetingOwnerId);
        if (changed)
        {
            item.MeetingId = null;
            ResetUpload(item, false);
            item.UploadSessionNumber = 1;
        }
        item.BaseUrl = canonical;
        item.OrganizationId = connection.OrganizationId;
        item.ApiKeyId = connection.ApiKeyId;
        item.MeetingOwnerId = connection.MeetingOwnerId;
    }

    private static int[] ValidateStatus(PendingUploadState item, UploadStatusResponse status)
    {
        if (status.UploadId != item.UploadId) throw new InvalidDataException("Status dotyczy innego uploadId.");
        var missing = status.MissingChunks ?? throw new InvalidDataException("Brak missingChunks.");
        if (missing.Distinct().Count() != missing.Length || missing.Any(i => i < 0 || i >= item.TotalChunks))
            throw new InvalidDataException("Niepoprawne indeksy missingChunks.");
        return missing;
    }

    internal static void ValidateUploadGeometry(long fileLength, Guid uploadId, int chunkSize, int totalChunks)
    {
        if (uploadId == Guid.Empty || chunkSize is < MinChunkSize or > MaxChunkSize)
            throw new InvalidDataException("Niepoprawna geometria uploadu RecoWilk.");
        var expected = checked((int)((fileLength + chunkSize - 1) / chunkSize));
        if (totalChunks != expected) throw new InvalidDataException("Liczba fragmentów nie odpowiada rozmiarowi pliku.");
    }

    internal static void ValidateLocalFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is < 1 or > MaxFileSize)
            throw new InvalidDataException("Lokalny MP3 ma nieobsługiwany rozmiar albo nie istnieje.");
    }

    private void ApplyFailure(PendingUploadState item, Exception exception)
    {
        item.Attempts++;
        item.LastErrorCategory = Category(exception);
        item.LastHttpStatusCode = exception is RecowilkApiException apiStatus ? (int)apiStatus.StatusCode : null;
        item.LastTraceId = exception is RecowilkApiException apiTrace ? apiTrace.TraceId : null;
        if (exception is RecowilkApiException api && api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            item.Stage = UploadStage.WaitingForCredentials;
            item.NextAttemptAt = DateTimeOffset.MaxValue;
            return;
        }
        if (IsPermanent(exception))
        {
            item.Stage = UploadStage.PermanentFailure;
            item.NextAttemptAt = DateTimeOffset.MaxValue;
            return;
        }
        var requested = exception is RecowilkApiException retry ? retry.RetryAfter : null;
        var seconds = Math.Min(1800, Math.Pow(2, Math.Min(item.Attempts, 10)) + Random.Shared.NextDouble());
        var delay = requested is { } value && value > TimeSpan.Zero
            ? TimeSpan.FromSeconds(Math.Min(1800, value.TotalSeconds)) : TimeSpan.FromSeconds(seconds);
        item.NextAttemptAt = DateTimeOffset.UtcNow.Add(delay);
    }

    private void EnsureCatalog(PendingUploadState item)
    {
        TryCatalog(() =>
        {
            if (_catalog.Get(item.RecordingId) is null)
                _catalog.Upsert(item.ToCatalogEntry(RecordingExportStatus.Queued));
        }, item.RecordingId);
    }

    private void ImportPendingCatalogEntries()
    {
        foreach (var path in Directory.EnumerateFiles(_queueDirectory, "*.upload"))
        {
            try { EnsureCatalog(Load(path)); }
            catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or InvalidDataException or FormatException)
            {
                Quarantine(path);
                _logger.LogWarning("Uszkodzony wpis kolejki RecoWilk został przeniesiony do kwarantanny podczas migracji katalogu");
            }
        }
    }

    private void UpdateCatalogFailure(PendingUploadState item)
    {
        var status = item.Stage switch
        {
            UploadStage.WaitingForCredentials => RecordingExportStatus.WaitingForCredentials,
            UploadStage.PermanentFailure => File.Exists(item.AudioPath)
                ? RecordingExportStatus.PermanentFailure : RecordingExportStatus.MissingFile,
            _ => RecordingExportStatus.RetryScheduled
        };
        UpdateCatalog(item, status);
    }

    private bool UpdateCatalog(PendingUploadState item, RecordingExportStatus status, bool exported = false)
    {
        return TryCatalog(() =>
        {
            var entry = _catalog.Get(item.RecordingId) ?? item.ToCatalogEntry(status);
            entry.AudioSizeBytes = File.Exists(item.AudioPath) ? new FileInfo(item.AudioPath).Length : entry.AudioSizeBytes;
            entry.ExportStatus = status;
            entry.MeetingId = item.MeetingId;
            entry.UploadId = item.UploadId;
            entry.AudioAssetId = item.AudioAssetId;
            entry.ProcessingJobId = item.ProcessingJobId;
            entry.UploadedChunks = item.UploadedChunks;
            entry.TotalChunks = item.TotalChunks;
            entry.Attempts = item.Attempts;
            entry.LastAttemptAt = item.LastAttemptAt;
            entry.NextAttemptAt = status == RecordingExportStatus.RetryScheduled ? item.NextAttemptAt : null;
            entry.ErrorCategory = item.LastErrorCategory;
            entry.HttpStatusCode = item.LastHttpStatusCode;
            entry.TraceId = item.LastTraceId;
            if (exported)
            {
                entry.ExportedAt = DateTimeOffset.UtcNow;
                entry.UploadedChunks = entry.TotalChunks;
                entry.ErrorCategory = null;
                entry.HttpStatusCode = null;
                entry.TraceId = null;
                entry.NextAttemptAt = null;
            }
            _catalog.Upsert(entry);
        }, item.RecordingId);
    }

    private bool TryCatalog(Action action, Guid recordingId)
    {
        try { action(); return true; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nie udało się zaktualizować katalogu nagrania {RecordingId}; kolejka eksportu pozostaje zachowana", recordingId);
            return false;
        }
    }

    private static bool IsPermanent(Exception exception)
    {
        if (exception is InvalidDataException or JsonException or CryptographicException) return true;
        return exception is RecowilkApiException api && (int)api.StatusCode is >= 400 and < 500
            && api.StatusCode is not HttpStatusCode.RequestTimeout and not HttpStatusCode.Conflict
            and not HttpStatusCode.Gone and not HttpStatusCode.TooManyRequests and not HttpStatusCode.UnprocessableEntity;
    }

    private static string Category(Exception exception) => exception switch
    {
        RecowilkApiException api when api.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "credentials",
        RecowilkApiException api when api.StatusCode == HttpStatusCode.TooManyRequests => "rate-limit",
        RecowilkApiException api when (int)api.StatusCode >= 500 => "server",
        RecowilkApiException => "api",
        HttpRequestException or TaskCanceledException => "network",
        IOException => "io",
        _ => "invalid-data"
    };

    private static RecowilkApiException ConnectionException(RecowilkConnectionResult result) => result.Failure switch
    {
        RecowilkConnectionFailure.Unauthorized => new(HttpStatusCode.Unauthorized, null, result.TraceId, result.RetryAfter),
        RecowilkConnectionFailure.Forbidden => new(HttpStatusCode.Forbidden, null, result.TraceId, result.RetryAfter),
        RecowilkConnectionFailure.RateLimited => new(HttpStatusCode.TooManyRequests, null, result.TraceId, result.RetryAfter),
        RecowilkConnectionFailure.ServerError => new(HttpStatusCode.ServiceUnavailable, null, result.TraceId, result.RetryAfter),
        RecowilkConnectionFailure.NetworkError => new(HttpStatusCode.RequestTimeout, null, result.TraceId, result.RetryAfter),
        _ => new(HttpStatusCode.BadGateway, "invalid_ping_response", result.TraceId, result.RetryAfter)
    };

    private static bool CodeIs(RecowilkApiException ex, string code) => string.Equals(ex.Code, code, StringComparison.OrdinalIgnoreCase);
    private static bool IsChunkRefresh(RecowilkApiException ex) =>
        ex.StatusCode == HttpStatusCode.Conflict && (CodeIs(ex, "upload_concurrency_conflict") || CodeIs(ex, "chunk_checksum_mismatch"))
        || ex.StatusCode == HttpStatusCode.UnprocessableEntity && CodeIs(ex, "chunk_size_mismatch");

    private static void ResetUpload(PendingUploadState item, bool incrementSession)
    {
        item.UploadId = null; item.ChunkSize = 0; item.TotalChunks = 0; item.ExpiresAt = null;
        item.Stage = UploadStage.InitializingUpload;
        if (incrementSession) item.UploadSessionNumber++;
    }

    private static string NormalizeTitle(string? title, string path)
    {
        var value = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(path) : title.Trim();
        if (string.IsNullOrEmpty(value)) value = "Nagranie spotkania";
        return Truncate(value, 500)!;
    }
    private static string? Truncate(string? value, int length) => string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value[..length];

    internal static bool TryBaseUri(string? value, out Uri result)
    {
        if (Uri.TryCreate(value?.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        { result = uri; return true; }
        result = null!; return false;
    }

    private static string CanonicalBaseUrl(Uri uri)
    {
        var builder = new UriBuilder(uri) { Host = uri.Host.ToLowerInvariant(), Path = uri.AbsolutePath.TrimEnd('/') + "/" };
        if (uri.IsDefaultPort) builder.Port = -1;
        return builder.Uri.AbsoluteUri;
    }

    private void Save(PendingUploadState item)
    {
        Directory.CreateDirectory(_queueDirectory);
        item.SchemaVersion = SchemaVersion;
        var path = QueuePath(item.RecordingId);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var protectedBytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(item, JsonOptions));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(protectedBytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private PendingUploadState Load(string path)
    {
        var item = JsonSerializer.Deserialize<PendingUploadState>(_protector.Unprotect(File.ReadAllBytes(path)), JsonOptions)
            ?? throw new InvalidDataException("Pusty wpis kolejki.");
        if (item.SchemaVersion != SchemaVersion || item.RecordingId == Guid.Empty || string.IsNullOrWhiteSpace(item.AudioPath))
            throw new InvalidDataException("Nieobsługiwana wersja wpisu kolejki.");
        return item;
    }

    private string QueuePath(Guid id) => Path.Combine(_queueDirectory, $"{id:N}.upload");

    private void RecoverTemporaryFiles()
    {
        foreach (var path in Directory.EnumerateFiles(_queueDirectory, "*.tmp"))
        {
            try
            {
                var item = Load(path); var target = QueuePath(item.RecordingId);
                if (!File.Exists(target)) File.Move(path, target); else File.Delete(path);
            }
            catch { Quarantine(path); }
        }
    }

    private void MigrateLegacyFiles()
    {
        foreach (var path in Directory.EnumerateFiles(_queueDirectory, "*.json"))
        {
            try
            {
                var old = JsonSerializer.Deserialize<LegacyPendingUpload>(File.ReadAllText(path), JsonOptions)
                    ?? throw new InvalidDataException("Pusty wpis v1.");
                Save(new PendingUploadState
                {
                    RecordingId = old.RecordingId,
                    AudioPath = old.AudioPath,
                    StartedAt = old.StartedAt,
                    StoppedAt = old.StoppedAt,
                    DurationMs = old.DurationMs,
                    Source = old.Source,
                    NextAttemptAt = DateTimeOffset.UtcNow
                });
                File.Delete(path);
            }
            catch { Quarantine(path); }
        }
    }

    private static void Quarantine(string path)
    {
        if (File.Exists(path)) File.Move(path, path + ".corrupt." + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref _reactivateWaiting, 1);
        Wake();
    }

    private void ReactivateWaitingItems()
    {
        foreach (var path in Directory.EnumerateFiles(_queueDirectory, "*.upload"))
        {
            try
            {
                var item = Load(path);
                if (item.Stage == UploadStage.WaitingForCredentials)
                {
                    item.NextAttemptAt = DateTimeOffset.UtcNow;
                    Save(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nie udało się reaktywować wpisu RecoWilk po zmianie ustawień");
            }
        }
    }

    private void Wake()
    {
        try { if (_signal.CurrentCount == 0) _signal.Release(); }
        catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _settings.SettingsChanged -= OnSettingsChanged;
        _stop.Cancel(); Wake();
        if (_worker is not null)
            try { await _worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _signal.Dispose(); _processGate.Dispose(); _stop.Dispose();
    }
}

internal interface IRecowilkQueueProtector { byte[] Protect(byte[] value); byte[] Unprotect(byte[] value); }
internal sealed class DpapiRecowilkQueueProtector : IRecowilkQueueProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MeetingAudioRecorder.Recowilk.Queue.v2");
    public byte[] Protect(byte[] value) => ProtectedData.Protect(value, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] value) => ProtectedData.Unprotect(value, Entropy, DataProtectionScope.CurrentUser);
}
internal enum UploadStage { WaitingForCredentials, CreatingMeeting, InitializingUpload, UploadingChunks, Completing, Completed, PermanentFailure }
internal sealed class PendingUploadState
{
    public int SchemaVersion { get; set; } = 2;
    public Guid RecordingId { get; set; }
    public required string AudioPath { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public long DurationMs { get; set; }
    public RecordingSourceContext? Source { get; set; }
    public string? BaseUrl { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? ApiKeyId { get; set; }
    public Guid? MeetingOwnerId { get; set; }
    public UploadStage Stage { get; set; } = UploadStage.CreatingMeeting;
    public Guid? MeetingId { get; set; }
    public Guid? UploadId { get; set; }
    public int UploadSessionNumber { get; set; } = 1;
    public int ChunkSize { get; set; }
    public int TotalChunks { get; set; }
    public int UploadedChunks { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastErrorCategory { get; set; }
    public int? LastHttpStatusCode { get; set; }
    public string? LastTraceId { get; set; }
    public Guid? AudioAssetId { get; set; }
    public Guid? ProcessingJobId { get; set; }
    public static PendingUploadState From(RecordingCompletedEventArgs value) => new()
    {
        RecordingId = value.Result.RecordingId,
        AudioPath = value.Result.OutputPath!,
        StartedAt = value.Session.StartedAt,
        StoppedAt = value.Session.StoppedAt,
        DurationMs = (long)value.Result.Duration.TotalMilliseconds,
        Source = value.Session.SourceContext,
        NextAttemptAt = DateTimeOffset.UtcNow
    };

    public static PendingUploadState From(RecordingCatalogEntry value) => new()
    {
        RecordingId = value.RecordingId,
        AudioPath = value.AudioPath,
        StartedAt = value.StartedAt,
        StoppedAt = value.StoppedAt,
        DurationMs = value.DurationMs,
        Source = new RecordingSourceContext(value.Provider, value.Client, value.ExternalEventId,
            value.MeetingUrl, value.Title, value.Description, value.ScheduledAt,
            value.Participants.Select(p => new GoogleMeetingAttendee(p.DisplayName, p.Email,
                string.Equals(p.Role, "Organizer", StringComparison.OrdinalIgnoreCase))).ToArray()),
        MeetingId = value.MeetingId,
        UploadId = value.UploadId,
        AudioAssetId = value.AudioAssetId,
        ProcessingJobId = value.ProcessingJobId,
        TotalChunks = value.TotalChunks,
        UploadedChunks = value.UploadedChunks,
        NextAttemptAt = DateTimeOffset.UtcNow
    };

    public RecordingCatalogEntry ToCatalogEntry(RecordingExportStatus status) => new()
    {
        RecordingId = RecordingId,
        AudioPath = AudioPath,
        AudioSizeBytes = File.Exists(AudioPath) ? new FileInfo(AudioPath).Length : 0,
        StartedAt = StartedAt,
        StoppedAt = StoppedAt,
        DurationMs = DurationMs,
        Title = string.IsNullOrWhiteSpace(Source?.Title) ? Path.GetFileNameWithoutExtension(AudioPath) : Source.Title,
        Description = Source?.Description,
        ScheduledAt = Source?.ScheduledAt,
        Provider = Source?.Provider ?? "ManualRecorder",
        Client = Source?.Client ?? "MeetingAudioRecorder",
        ExternalEventId = Source?.ExternalEventId,
        MeetingUrl = Source?.MeetingUrl,
        Participants = Source?.Participants.Select(p => new RecordingCatalogParticipant(p.DisplayName, p.Email,
            p.IsOrganizer ? "Organizer" : "Attendee")).ToArray() ?? [],
        ExportStatus = status,
        MeetingId = MeetingId,
        UploadId = UploadId,
        AudioAssetId = AudioAssetId,
        ProcessingJobId = ProcessingJobId,
        UploadedChunks = UploadedChunks,
        TotalChunks = TotalChunks,
        Attempts = Attempts,
        LastAttemptAt = LastAttemptAt,
        NextAttemptAt = NextAttemptAt,
        ErrorCategory = LastErrorCategory,
        HttpStatusCode = LastHttpStatusCode,
        TraceId = LastTraceId
    };
}

internal sealed class NullRecordingCatalog : IRecordingCatalog
{
    public static NullRecordingCatalog Instance { get; } = new();
    public event EventHandler<RecordingCatalogChangedEventArgs>? Changed { add { } remove { } }
    public IReadOnlyList<RecordingCatalogEntry> List() => [];
    public RecordingCatalogEntry? Get(Guid recordingId) => null;
    public void Upsert(RecordingCatalogEntry entry) { }
    public void ReconcileRecordingsDirectory(string directory) { }
}
internal sealed class LegacyPendingUpload
{
    public Guid RecordingId { get; set; }
    public required string AudioPath { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public long DurationMs { get; set; }
    public RecordingSourceContext? Source { get; set; }
}
