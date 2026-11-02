using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Infrastructure.Translation;

public sealed class LocalTranslationWorker(string executable)
{
    public async Task RunAsync(TranslationWorkerStart config, Action<TranslationWorkerEvent> receive, CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        var name = "mar-translate-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        info.ArgumentList.Add("--pipe"); info.ArgumentList.Add(name);
        using var process = Process.Start(info) ?? throw new IOException("Nie można uruchomić tłumacza.");
        // Native diagnostics can include sensitive text. Drain, never persist or forward to application logs.
        async Task Drain(StreamReader stream) { var buffer = new char[4096]; while (await stream.ReadAsync(buffer) > 0) { } }
        var stderr = Drain(process.StandardError); var stdout = Drain(process.StandardOutput);
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(stop);
            connect.CancelAfter(TimeSpan.FromSeconds(30));
            await pipe.WaitForConnectionAsync(connect.Token);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(config));
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = stop.Register(() => stopped.TrySetResult());
            async Task ReadEvents()
            {
                bool ended = false;
                while (await reader.ReadLineAsync() is { } line)
                {
                    var value = JsonSerializer.Deserialize<TranslationWorkerEvent>(line) ?? throw new InvalidDataException("Nieprawidłowy komunikat tłumacza.");
                    receive(value);
                    if (value.Kind == "ended")
                    {
                        ended = true;
                        if (value.Message is not null) throw new InvalidOperationException(value.Message);
                        break;
                    }
                }
                if (!ended) throw new IOException("Silnik tłumaczenia zakończył się niespodziewanie. Zapisane frazy zachowano.");
            }
            var read = ReadEvents();
            if (await Task.WhenAny(read, stopped.Task) == stopped.Task)
            {
                await writer.WriteLineAsync("stop");
                await read.WaitAsync(TimeSpan.FromSeconds(30));
            }
            else await read;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (process.ExitCode != 0) throw new IOException("Silnik tłumaczenia zakończył się błędem.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stderr, stdout);
        }
    }
}
