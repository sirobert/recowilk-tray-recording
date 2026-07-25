using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IRecordingSessionManifestStore
{
    void Save(RecordingSessionManifest manifest);
    RecordingSessionManifest? TryLoad(Guid recordingId);
    void Delete(Guid recordingId);
}

