using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IRecordingRecoveryService
{
    IReadOnlyList<RecoverableRecording> FindRecoverableRecordings();
    void DeleteRecoverable(RecoverableRecording recoverable);
    void OpenTempFolder();
}
