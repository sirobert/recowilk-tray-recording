namespace MeetingAudioRecorder.BrowserBridge;

internal static class NativeMessageStreamReader
{
    public static async Task<bool> ReadExactOrEndAsync(Stream stream, Memory<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[total..]);
            if (read == 0)
                return false;
            total += read;
        }

        return true;
    }
}
