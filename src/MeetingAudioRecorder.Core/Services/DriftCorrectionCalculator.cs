using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Services;

public static class DriftCorrectionCalculator
{
    public static DriftCorrectionPlan Calculate(
        long sourceFrames,
        long targetFrames,
        int nominalSampleRate,
        double maximumCorrectionPpm,
        TimeSpan minimumDrift)
    {
        if (sourceFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceFrames));
        if (targetFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetFrames));
        if (nominalSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(nominalSampleRate));
        if (maximumCorrectionPpm < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCorrectionPpm));
        if (minimumDrift < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumDrift));

        var driftFrames = sourceFrames - targetFrames;
        var measuredPpm = driftFrames / (double)targetFrames * 1_000_000;
        var minimumDriftFrames = minimumDrift.TotalSeconds * nominalSampleRate;
        var shouldCorrect = Math.Abs(driftFrames) >= minimumDriftFrames && driftFrames != 0;
        var appliedPpm = shouldCorrect
            ? Math.Clamp(measuredPpm, -maximumCorrectionPpm, maximumCorrectionPpm)
            : 0;
        var effectiveInputRate = nominalSampleRate * (1 + appliedPpm / 1_000_000);

        return new DriftCorrectionPlan(
            ShouldCorrect: shouldCorrect,
            WasLimited: shouldCorrect && Math.Abs(measuredPpm) > maximumCorrectionPpm,
            SourceFrames: sourceFrames,
            TargetFrames: targetFrames,
            DriftFrames: driftFrames,
            MeasuredDriftPpm: measuredPpm,
            AppliedCorrectionPpm: appliedPpm,
            EffectiveInputRate: effectiveInputRate);
    }
}
