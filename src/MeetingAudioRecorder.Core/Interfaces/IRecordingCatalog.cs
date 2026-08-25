using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IRecordingCatalog
{
    event EventHandler<RecordingCatalogChangedEventArgs>? Changed;
    IReadOnlyList<RecordingCatalogEntry> List();
    RecordingCatalogEntry? Get(Guid recordingId);
    void Upsert(RecordingCatalogEntry entry);
    void ReconcileRecordingsDirectory(string directory);
}
