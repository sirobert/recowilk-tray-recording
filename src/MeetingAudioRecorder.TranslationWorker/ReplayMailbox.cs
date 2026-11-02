namespace MeetingAudioRecorder.TranslationWorker;

// Bounded commit queue. A fault invalidates queued and late results; no DropOldest.
public sealed class ReplayMailbox<T> where T : class
{
    readonly object gate = new();
    readonly Queue<(T Value, double At)> queue = new();
    string? fault;
    bool complete;
    int maximum, discarded;
    public string? Fault { get { lock (gate) return fault; } }
    public int MaximumDepth { get { lock (gate) return maximum; } }
    public int Discarded { get { lock (gate) return discarded; } }
    public bool Finished { get { lock (gate) return complete && queue.Count == 0; } }
    public bool Enqueue(T value, double now)
    {
        lock (gate)
        {
            if (fault is not null || complete) { discarded++; return false; }
            if (queue.Count >= 3) { Fail("queue-capacity"); discarded++; return false; }
            queue.Enqueue((value, now)); maximum = Math.Max(maximum, queue.Count); return true;
        }
    }
    public T? Take(double now)
    {
        lock (gate)
        {
            CheckAgeLocked(now);
            return queue.Count > 0 ? queue.Dequeue().Value : null;
        }
    }
    public void CheckAge(double now) { lock (gate) CheckAgeLocked(now); }
    void CheckAgeLocked(double now) { if (queue.TryPeek(out var item) && now - item.At > 2000) Fail("queue-age"); }
    public void Complete() { lock (gate) complete = true; }
    public void Fail(string reason)
    {
        lock (gate) { fault ??= reason; discarded += queue.Count; queue.Clear(); complete = true; }
    }
    public bool Publish(Action action) { lock (gate) { if (fault is not null) return false; action(); return true; } }
}
