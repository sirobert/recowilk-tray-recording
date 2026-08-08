namespace MeetingAudioRecorder.Core.Models;

public sealed record GoogleConnectionInfo(
    bool IsConnected,
    string? AccountEmail,
    string? AccountUserId);
