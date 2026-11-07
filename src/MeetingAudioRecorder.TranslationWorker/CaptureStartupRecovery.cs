namespace MeetingAudioRecorder.TranslationWorker;

internal static class CaptureStartupRecovery
{
    public static async Task<T> Run<T>(Func<Task<T>> attempt, Func<bool> receivedAudio, Action retrying, CancellationToken stop)
    {
        for (int number = 1; ; number++)
        {
            stop.ThrowIfCancellationRequested();
            try { return await attempt(); }
            catch (Exception ex) when (IsDeviceInvalidated(ex) && !receivedAudio() && number < 3 && !stop.IsCancellationRequested)
            {
                // The attempt must dispose its old WASAPI objects before returning here.
                retrying();
                await Task.Delay(500, stop);
            }
        }
    }

    public static bool IsDeviceInvalidated(Exception error) =>
        error.HResult == unchecked((int)0x88890004)
        || (error is AggregateException aggregate && aggregate.InnerExceptions.Any(IsDeviceInvalidated))
        || (error.InnerException is { } inner && IsDeviceInvalidated(inner));

    public static string Describe(Exception error, bool systemAudio)
    {
        string source = systemAudio ? "Wyjście audio" : "Mikrofon";
        return IsDeviceInvalidated(error)
            ? $"{source}: Windows rozłączył lub przekonfigurował urządzenie (0x88890004). Sprawdź połączenie i wybór urządzeń w ustawieniach Recordera, następnie uruchom tłumaczenie ponownie."
            : $"{source}: {error.Message}";
    }
}
