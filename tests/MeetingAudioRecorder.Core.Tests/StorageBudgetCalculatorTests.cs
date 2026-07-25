using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Services;

namespace MeetingAudioRecorder.Core.Tests;

public class StorageBudgetCalculatorTests
{
    [Fact]
    public void CaptureReserve_UsesActualFormatsAndSafetyMargin()
    {
        var format = new WaveFormatInfo
        {
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 32,
            Encoding = "IeeeFloat"
        };

        var bytes = StorageBudgetCalculator.CalculateCaptureReserve(
            format,
            format,
            TimeSpan.FromMinutes(2));

        Assert.Equal(2L * 48000 * 2 * 4 * 120 + StorageBudgetCalculator.CaptureSafetyBytes, bytes);
    }

    [Fact]
    public void ProcessingBudget_OneHour_IncludesMixedWavAndMp3()
    {
        var budget = StorageBudgetCalculator.CalculateProcessingBudget(
            TimeSpan.FromHours(1),
            sourceBytes: 0,
            targetSampleRate: 48000,
            bitrateKbps: 192,
            keepSeparateTracks: false);

        Assert.Equal(48000L * 2 * 2 * 3600 + StorageBudgetCalculator.ProcessingTempSafetyBytes,
            budget.TempAdditionalBytes);
        Assert.Equal(192000L / 8 * 3600 + StorageBudgetCalculator.OutputSafetyBytes,
            budget.OutputAdditionalBytes);
    }

    [Fact]
    public void SeparateTracks_ReserveTwoAdditionalStereoWavs()
    {
        var withoutTracks = StorageBudgetCalculator.CalculateProcessingBudget(
            TimeSpan.FromMinutes(10), 0, 48000, 192, keepSeparateTracks: false);
        var withTracks = StorageBudgetCalculator.CalculateProcessingBudget(
            TimeSpan.FromMinutes(10), 0, 48000, 192, keepSeparateTracks: true);

        Assert.Equal(2L * 48000 * 2 * 2 * 600,
            withTracks.OutputAdditionalBytes - withoutTracks.OutputAdditionalBytes);
    }

    [Fact]
    public void UnknownDuration_UsesConservativeSourceSizeFallback()
    {
        const long sourceBytes = 100 * 1024 * 1024;

        var budget = StorageBudgetCalculator.CalculateProcessingBudget(
            TimeSpan.Zero, sourceBytes, 48000, 192, keepSeparateTracks: false);

        Assert.True(budget.TempAdditionalBytes >= sourceBytes * 3);
        Assert.True(budget.OutputAdditionalBytes >= StorageBudgetCalculator.OutputSafetyBytes);
    }
}
