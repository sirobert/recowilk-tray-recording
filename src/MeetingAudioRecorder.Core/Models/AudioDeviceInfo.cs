namespace MeetingAudioRecorder.Core.Models;

/// <summary>
/// Opis urządzenia audio (endpoint MMDevice).
/// </summary>
public sealed class AudioDeviceInfo
{
    public required string Id { get; init; }
    public required string FriendlyName { get; init; }
    public string? Description { get; init; }
    public AudioDeviceType DeviceType { get; init; }
    public bool IsActive { get; init; }
    public bool IsDefault { get; init; }
    public bool IsDefaultCommunications { get; init; }

    public override string ToString() => FriendlyName;
}

public enum AudioDeviceType
{
    Capture = 0,
    Render = 1
}
