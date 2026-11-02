namespace MeetingAudioRecorder.TranslationWorker;

// Replace pending previews only. Committed audio always uses the separate reliable queue.
public sealed class LatestDraft<T> where T : class
{
    readonly object gate = new();
    T? pending;
    int pendingId, closedThrough;
    public void Offer(int id, T value) { lock (gate) { if (id <= closedThrough) return; pendingId = id; pending = value; } }
    public T? Take() { lock (gate) { var value = pending; pending = null; return value; } }
    public void CloseThrough(int id) { lock (gate) { closedThrough = Math.Max(closedThrough, id); if (pendingId <= closedThrough) pending = null; } }
    public bool IsOpen(int id) { lock (gate) return id > closedThrough; }
    public bool Publish(int id, Action action) { lock (gate) { if (id <= closedThrough) return false; action(); return true; } }
}
