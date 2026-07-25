namespace MeetingAudioRecorder.Core.Models;

public sealed class WavRepairResult
{
    public required bool Success { get; init; }
    public required string OutputPath { get; init; }
    public required long DataLengthBytes { get; init; }
    public required int BlockAlign { get; init; }
}

