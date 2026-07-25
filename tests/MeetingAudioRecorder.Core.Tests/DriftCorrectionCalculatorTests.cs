using MeetingAudioRecorder.Core.Services;

namespace MeetingAudioRecorder.Core.Tests;

public class DriftCorrectionCalculatorTests
{
    [Fact]
    public void FourHourTrack_AtFiveHundredPpm_IsFullyCorrected()
    {
        const int sampleRate = 48000;
        var targetFrames = 4L * 60 * 60 * sampleRate;
        var sourceFrames = targetFrames + targetFrames * 500 / 1_000_000;

        var plan = DriftCorrectionCalculator.Calculate(
            sourceFrames,
            targetFrames,
            sampleRate,
            maximumCorrectionPpm: 1000,
            minimumDrift: TimeSpan.FromMilliseconds(50));

        Assert.True(plan.ShouldCorrect);
        Assert.False(plan.WasLimited);
        Assert.InRange(plan.AppliedCorrectionPpm, 499.9, 500.1);
        Assert.InRange(plan.EffectiveInputRate, 48023.99, 48024.01);
    }

    [Fact]
    public void ExcessiveDrift_IsLimitedToConfiguredRate()
    {
        const int sampleRate = 48000;
        var targetFrames = 60L * sampleRate;
        var sourceFrames = targetFrames + targetFrames * 5000 / 1_000_000;

        var plan = DriftCorrectionCalculator.Calculate(
            sourceFrames,
            targetFrames,
            sampleRate,
            maximumCorrectionPpm: 1000,
            minimumDrift: TimeSpan.Zero);

        Assert.True(plan.ShouldCorrect);
        Assert.True(plan.WasLimited);
        Assert.Equal(1000, plan.AppliedCorrectionPpm, precision: 3);
    }

    [Fact]
    public void SubThresholdDifference_DoesNotResampleForDrift()
    {
        const int sampleRate = 48000;
        var targetFrames = 60L * sampleRate;
        var sourceFrames = targetFrames + sampleRate / 100;

        var plan = DriftCorrectionCalculator.Calculate(
            sourceFrames,
            targetFrames,
            sampleRate,
            maximumCorrectionPpm: 1000,
            minimumDrift: TimeSpan.FromMilliseconds(50));

        Assert.False(plan.ShouldCorrect);
        Assert.Equal(sampleRate, plan.EffectiveInputRate);
    }

    [Fact]
    public void ShorterTrack_ProducesNegativeCorrection()
    {
        const int sampleRate = 44100;
        var targetFrames = 10L * 60 * sampleRate;
        var sourceFrames = targetFrames - targetFrames * 250 / 1_000_000;

        var plan = DriftCorrectionCalculator.Calculate(
            sourceFrames,
            targetFrames,
            sampleRate,
            maximumCorrectionPpm: 1000,
            minimumDrift: TimeSpan.Zero);

        Assert.InRange(plan.AppliedCorrectionPpm, -250.1, -249.9);
        Assert.True(plan.EffectiveInputRate < sampleRate);
    }
}
