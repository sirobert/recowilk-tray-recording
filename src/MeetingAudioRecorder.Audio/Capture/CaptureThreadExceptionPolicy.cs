namespace MeetingAudioRecorder.Audio.Capture;

internal static class CaptureThreadExceptionPolicy
{
    public static Exception? Execute(Action captureLoop, Action stopAudioClient)
    {
        ArgumentNullException.ThrowIfNull(captureLoop);
        ArgumentNullException.ThrowIfNull(stopAudioClient);

        Exception? captureError = null;
        try
        {
            captureLoop();
        }
        catch (Exception ex)
        {
            captureError = ex;
        }

        Exception? stopError = null;
        try
        {
            stopAudioClient();
        }
        catch (Exception ex)
        {
            stopError = ex;
        }

        return (captureError, stopError) switch
        {
            (null, null) => null,
            (not null, null) => captureError,
            (null, not null) => stopError,
            _ => new AggregateException(
                "Przechwytywanie i zatrzymanie klienta audio zakończyły się błędem.",
                captureError!,
                stopError!)
        };
    }
}
