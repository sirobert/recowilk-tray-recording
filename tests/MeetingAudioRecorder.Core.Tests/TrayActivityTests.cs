using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Tests;

public class TrayActivityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingHistoricalFilesAreInformationalNotGlobalAlarm(bool enabled)
    {
        var upload = UploadActivity.From([Entry(RecordingExportStatus.MissingFile)], enabled);
        Assert.False(upload.NeedsAttention);
        Assert.Contains("brak pliku: 1", upload.Text);
        Assert.False(new RecorderTrayStatus { Upload = upload }.NeedsAttention);
    }

    [Theory]
    [InlineData(RecordingExportStatus.PermanentFailure)]
    [InlineData(RecordingExportStatus.WaitingForCredentials)]
    public void MissingFilesDoNotHideActualExportProblems(RecordingExportStatus problem)
    {
        var upload = UploadActivity.From([Entry(RecordingExportStatus.MissingFile), Entry(problem)], true);
        Assert.True(upload.NeedsAttention);
        Assert.Contains("brak pliku: 1", upload.Text);
        Assert.Contains("wymaga uwagi: 1", upload.Text);
    }
    [Fact]
    public void LongRecordingDurationDoesNotWrapAt24Hours()
    {
        var status = new RecorderTrayStatus { Recording = AppRecordingState.Recording, Duration = new TimeSpan(27, 2, 3) };
        Assert.Contains("27:02:03", status.RecordingText);
    }

    [Fact]
    public void UploadErrorDoesNotClearEitherActivityMarker()
    {
        var status = new RecorderTrayStatus
        {
            Recording = AppRecordingState.Recording,
            Translation = new(TranslationActivityState.Running),
            Upload = new("błąd", true)
        };
        Assert.True(status.RecordingMarker); Assert.True(status.TranslationMarker); Assert.True(status.NeedsAttention);
        var stopped = status with { Translation = new() };
        Assert.True(stopped.RecordingMarker); Assert.False(stopped.TranslationMarker); Assert.True(stopped.NeedsAttention);
    }
    [Fact]
    public void RecordingAndTranslationRemainVisibleTogether()
    {
        var status = new RecorderTrayStatus { Recording = AppRecordingState.Recording, Duration = TimeSpan.FromSeconds(754), Translation = new(TranslationActivityState.Running, "wyjście + mikrofon") };
        Assert.True(status.RecordingMarker); Assert.True(status.TranslationMarker); Assert.False(status.NeedsAttention);
        Assert.Contains("12:34", status.Tooltip); Assert.Contains("wyjście + mikrofon", status.Tooltip);
    }

    [Fact]
    public void TranslationFailureDoesNotReplaceRecordingStatus()
    {
        var status = new RecorderTrayStatus { Recording = AppRecordingState.Recording, Translation = new(TranslationActivityState.Error, Detail: "Mikrofon odłączony") };
        Assert.True(status.RecordingMarker); Assert.True(status.NeedsAttention);
        Assert.Contains("Nagrywanie: trwa", status.Tooltip); Assert.Contains("Mikrofon odłączony", status.Tooltip);
    }

    [Fact]
    public void SavedRecordingReturnsToReadyWithoutClearingTranslation()
    {
        var status = new RecorderTrayStatus { Recording = AppRecordingState.Completed, Translation = new(TranslationActivityState.Stopping) };
        Assert.False(status.RecordingMarker); Assert.True(status.TranslationMarker);
        Assert.Contains("gotowe", status.RecordingText); Assert.Contains("kończenie", status.TranslationText);
    }

    [Fact]
    public void UploadSummaryKeepsActivePendingAndFailedCounts()
    {
        var active = Entry(RecordingExportStatus.Uploading, 2, 4);
        var upload = UploadActivity.From([
            active, Entry(RecordingExportStatus.Queued),
            Entry(RecordingExportStatus.PermanentFailure), Entry(RecordingExportStatus.Exported), Entry(RecordingExportStatus.LocalOnly)], true, active.RecordingId);
        Assert.True(upload.NeedsAttention); Assert.Contains("50%", upload.Text);
        Assert.Contains("oczekuje: 1", upload.Text); Assert.Contains("uwagi: 1", upload.Text);
    }

    [Fact]
    public void DisabledUploadsDoNotClaimStaleCatalogEntryIsTransferring()
    {
        var upload = UploadActivity.From([Entry(RecordingExportStatus.Uploading, 1, 2)], false);
        Assert.Contains("wyłączona", upload.Text); Assert.DoesNotContain("50%", upload.Text);
    }

    [Fact]
    public void PersistedUploadStageAfterRestartIsPendingUntilWorkerActuallyRuns()
    {
        var upload = UploadActivity.From([Entry(RecordingExportStatus.Uploading, 1, 2)], true);
        Assert.Contains("oczekuje: 1", upload.Text); Assert.DoesNotContain("wysyłanie:", upload.Text);
    }

    [Fact]
    public void RetryAndCredentialsAreDistinguishedFromActiveTransfer()
    {
        var upload = UploadActivity.From([Entry(RecordingExportStatus.RetryScheduled), Entry(RecordingExportStatus.WaitingForCredentials)], true);
        Assert.True(upload.NeedsAttention); Assert.Contains("oczekuje: 1", upload.Text); Assert.Contains("uwagi: 1", upload.Text);
        Assert.DoesNotContain("wysyłanie:", upload.Text);
    }

    private static RecordingCatalogEntry Entry(RecordingExportStatus status, int sent = 0, int total = 0) =>
        new() { RecordingId = Guid.NewGuid(), AudioPath = "test.mp3", ExportStatus = status, UploadedChunks = sent, TotalChunks = total };
}
