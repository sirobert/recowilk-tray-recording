namespace MeetingAudioRecorder.Core.Models;

public readonly record struct DriftCorrectionPlan(
    bool ShouldCorrect,
    bool WasLimited,
    long SourceFrames,
    long TargetFrames,
    long DriftFrames,
    double MeasuredDriftPpm,
    double AppliedCorrectionPpm,
    double EffectiveInputRate);

