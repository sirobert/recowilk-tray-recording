using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.TranslationWorker;

if (args.Length != 2 || args[0] != "--pipe") return 2;
using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
await pipe.ConnectAsync(15000);
using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
var config = JsonSerializer.Deserialize<TranslationWorkerStart>(await reader.ReadLineAsync() ?? throw new EndOfStreamException())!;
var sendGate = new object();
void Send(TranslationWorkerEvent value) { lock (sendGate) writer.WriteLine(JsonSerializer.Serialize(value)); }
void Emit(Caption value, bool draft) => Send(new("caption", Caption: new(config.Source, value.Id, value.English, value.Polish, draft, value.StartMs, value.EndMs, value.Status)));
var session = new LiveSession(config.ModelsDirectory, config.Turbo, text =>
{
    if (text.StartsWith("●") || text.StartsWith("▶")) Send(new("ready"));
    Send(new("status", text));
}, c => Emit(c, false), _ => { }, c => Emit(c, true));
var run = Task.Run(() => session.RunAsync(config.DeviceId, config.WavPath, config.Source == "output"));
_ = Task.Run(async () =>
{
    try { await reader.ReadLineAsync(); } catch (IOException) { }
    session.Stop(); // stop command OR parent gone/closed pipe
    if (await Task.WhenAny(run, Task.Delay(30000)) != run) Environment.Exit(3);
});
try
{
    await run;
    Send(new("ended", session.Fault));
    return session.Fault is null ? 0 : 1;
}
catch { return 1; }
