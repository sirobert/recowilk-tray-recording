using System.Diagnostics;
using MeetingAudioRecorder.BrowserBridge;

namespace MeetingAudioRecorder.Core.Tests;

public sealed class NativeMessageStreamReaderTests
{
    [Fact]
    public async Task CleanEndOfStream_ReturnsFalse()
    {
        await using var stream = new MemoryStream();

        var hasMessage = await NativeMessageStreamReader.ReadExactOrEndAsync(stream, new byte[4]);

        Assert.False(hasMessage);
    }

    [Fact]
    public async Task CompletePrefix_FillsBufferAndReturnsTrue()
    {
        await using var stream = new MemoryStream([1, 2, 3, 4]);
        var buffer = new byte[4];

        var hasMessage = await NativeMessageStreamReader.ReadExactOrEndAsync(stream, buffer);

        Assert.True(hasMessage);
        Assert.Equal([1, 2, 3, 4], buffer);
    }

    [Fact]
    public async Task NativeHost_ExitsAfterBrowserClosesStandardInput()
    {
        var assemblyPath = typeof(NativeMessageStreamReader).Assembly.Location;
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{assemblyPath}\" chrome-extension://eljjpmlmlnjjpjlnhiilfclkhoecdlij/",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Nie udało się uruchomić Native Messaging host.");

        process.StandardInput.Close();
        var exitTask = process.WaitForExitAsync();
        var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(2)));
        if (completed != exitTask)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        Assert.Same(exitTask, completed);
        Assert.Equal(0, process.ExitCode);
    }
}
