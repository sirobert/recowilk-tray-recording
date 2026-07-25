namespace MeetingAudioRecorder.Core.Models;

public readonly record struct StorageBudget(
    long TempAdditionalBytes,
    long OutputAdditionalBytes);

