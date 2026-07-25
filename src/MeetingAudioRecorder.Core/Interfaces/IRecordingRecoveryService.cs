using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IRecordingRecoveryService
{
    IReadOnlyList<RecoverableRecording> FindRecoverableRecordings();
    RecoverableRecording PrepareForRecovery(RecoverableRecording recoverable);
    void DeleteRecoverable(RecoverableRecording recoverable);
    void OpenTempFolder();
}
