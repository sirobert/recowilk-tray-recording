namespace MeetingAudioRecorder.Core.Interfaces;

public interface IMp3EncodingService
{
    Task EncodeToMp3Async(string inputWavPath, string outputMp3Path, int bitrateKbps, CancellationToken cancellationToken = default);
}
