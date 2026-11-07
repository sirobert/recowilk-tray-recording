using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingAudioRecorder.TranslationWorker;

// Captures the selected render endpoint's mix, never a microphone or a single app.
internal sealed class EventSyncLoopbackCapture(MMDevice device) : WasapiCapture(device, true, 20)
{
    protected override AudioClientStreamFlags GetAudioClientStreamFlags() => base.GetAudioClientStreamFlags() | AudioClientStreamFlags.Loopback;
}

internal static class EndpointCapture
{
    internal sealed record Stats(long CapturedBytes, long SilenceBytes);
    public static Task<Stats> Run(string id, bool systemAudio, Action<byte[], int> accept, Action idle, Action started, CancellationToken stop, Action? retrying = null)
    {
        int receivedAudio = 0;
        return CaptureStartupRecovery.Run(() => RunOnce(id, systemAudio, (bytes, count) =>
        {
            if (count > 0) Interlocked.Exchange(ref receivedAudio, 1);
            accept(bytes, count);
        }, idle, started, stop), () => Volatile.Read(ref receivedAudio) != 0, () => retrying?.Invoke(), stop);
    }

    private static async Task<Stats> RunOnce(string id, bool systemAudio, Action<byte[], int> accept, Action idle, Action started, CancellationToken stop)
    {
        using var enumerator = new MMDeviceEnumerator(); using var device = enumerator.GetDevice(id);
        if (device.DataFlow != (systemAudio ? DataFlow.Render : DataFlow.Capture)) throw new InvalidOperationException("Urządzenie nie pasuje do wybranego źródła.");
        using WasapiCapture capture = systemAudio ? new EventSyncLoopbackCapture(device) : new WasapiCapture(device, true, 20);
        capture.WaveFormat = new WaveFormat(16000, 16, 1);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new object(); var clock = Stopwatch.StartNew(); var idleBoundary = new CaptureIdleBoundary(); Exception? failure = null;
        long capturedBytes = 0, silenceBytes = 0;
        capture.DataAvailable += (_, e) =>
        {
            lock (gate)
            {
                if (e.BytesRecorded > 0) { capturedBytes += e.BytesRecorded; idleBoundary.Packet(clock.Elapsed.TotalMilliseconds); accept(e.Buffer, e.BytesRecorded); }
            }
        };
        capture.RecordingStopped += (_, e) => { failure = e.Exception; stopped.TrySetResult(); };
        if (stop.IsCancellationRequested) return new(0, 0);
        capture.StartRecording(); started();
        try
        {
            while (!stop.IsCancellationRequested && !stopped.Task.IsCompleted)
            {
                await Task.WhenAny(stopped.Task, Task.Delay(32, stop));
                // WASAPI loopback may deliver no packets once playback stops.
                // Close the utterance after wall-clock idle; do not invent PCM or extend the sample timeline.
                if (systemAudio && !stop.IsCancellationRequested && !stopped.Task.IsCompleted)
                    lock (gate)
                        if (idleBoundary.TryClose(clock.Elapsed.TotalMilliseconds)) idle();
            }
        }
        finally { capture.StopRecording(); await stopped.Task; }
        if (failure is not null) throw new InvalidOperationException("Urządzenie audio zostało odłączone lub zatrzymane.", failure);
        return new(capturedBytes, silenceBytes);
    }
}
