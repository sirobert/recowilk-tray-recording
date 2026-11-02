namespace MeetingAudioRecorder.TranslationWorker;

// Caller serializes this state with capture callbacks. Idle closes VAD; it never fabricates PCM.
public sealed class CaptureIdleBoundary
{
    private double lastPacket;
    private bool pending;
    public void Packet(double now) { lastPacket = now; pending = true; }
    public bool TryClose(double now)
    {
        if (!pending || now - lastPacket < 700) return false;
        pending = false; return true;
    }
}
