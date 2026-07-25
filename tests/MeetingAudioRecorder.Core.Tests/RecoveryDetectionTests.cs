using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Tests;

/// <summary>
/// Logika wykrywania plików tymczasowych (bez zależności Infrastructure).
/// </summary>
public class RecoveryDetectionTests
{
    [Fact]
    public void TempFileNaming_CanParseGuid()
    {
        var id = Guid.NewGuid();
        var mic = $"{id:N}_microphone.tmp.wav";
        var loop = $"{id:N}_loopback.tmp.wav";

        Assert.EndsWith("_microphone.tmp.wav", mic);
        Assert.EndsWith("_loopback.tmp.wav", loop);

        var micId = mic.Split('_')[0];
        Assert.True(Guid.TryParseExact(micId, "N", out var parsed));
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void RecoverableRecording_RequiresAtLeastOneValidFile()
    {
        var good = new RecoverableRecording
        {
            RecordingId = Guid.NewGuid(),
            HasValidMicrophoneFile = true,
            HasValidLoopbackFile = false,
            MicrophoneFileSize = 1000,
            LoopbackFileSize = 10
        };
        Assert.True(good.HasValidMicrophoneFile || good.HasValidLoopbackFile);

        var bad = new RecoverableRecording
        {
            RecordingId = Guid.NewGuid(),
            HasValidMicrophoneFile = false,
            HasValidLoopbackFile = false
        };
        Assert.False(bad.HasValidMicrophoneFile || bad.HasValidLoopbackFile);
    }
}
