using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

using Whisper.net;
using Whisper.net.LibraryLoader;

namespace MeetingAudioRecorder.TranslationWorker;

public sealed record Caption(int Id, string English, string Polish, string Status, double Seconds, double StartMs, double EndMs);
public sealed record AudioEndpoint(string Id, string Name);

// Prototype adapter around the same pinned engines/segmenter as the file probe.
public sealed class LiveSession(string root, bool turbo, Action<string> status, Action<Caption> caption, Action<double> level, Action<Caption>? draft = null)
{
    sealed record Packet(byte[] Bytes, double At);
    sealed record Request(int Id, float[] Audio, double At, double StartMs, double EndMs);
    readonly Channel<Packet> audio = Channel.CreateBounded<Packet>(new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
    readonly ReplayMailbox<Request> commits = new();
    readonly LatestDraft<Request> drafts = new();
    readonly object previewGate = new();
    CancellationTokenSource? previewCancellation;
    readonly CancellationTokenSource stop = new();
    readonly Stopwatch clock = new();
    int queuedBytes, submitted, published;
    string? fault;
    public int Published => Volatile.Read(ref published);
    public string? Fault => Volatile.Read(ref fault);
    public void Stop() { try { stop.Cancel(); } catch (ObjectDisposedException) { } }
    void Fail(string message)
    {
        if (Interlocked.CompareExchange(ref fault, message, null) is null) status(message);
        commits.Fail("session-fault"); stop.Cancel(); audio.Writer.TryComplete();
    }
    public static List<AudioEndpoint> Microphones() => Devices(false);
    public static List<AudioEndpoint> Devices(bool systemAudio)
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioEndpoint>();
        var flow = systemAudio ? DataFlow.Render : DataFlow.Capture;
        string? defaultId = null;
        try { using var preferred = enumerator.GetDefaultAudioEndpoint(flow, systemAudio ? Role.Multimedia : Role.Console); defaultId = preferred.ID; } catch { }
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            using (device) result.Add(new(device.ID, device.FriendlyName));
        return result.OrderByDescending(d => d.Id == defaultId).ThenBy(d => d.Name).ToList();
    }
    public async Task RunAsync(string? deviceId, string? wavPath, bool systemAudio = false)
    {
        // Entire lifetime runs off the WPF dispatcher, including native initialization.
        try
        {
            status("Ładowanie modeli… Źródła audio są wyłączone.");
            string models = root;
            string whisper = Path.Combine(models, turbo ? "ggml-large-v3-turbo-q8_0.bin" : "ggml-small.en-q8_0.bin");
            ModelFiles.Verify(whisper, turbo ? "317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1" : "67a179f608ea6114bd3fdb9060e762b588a3fb3bd00c4387971be4d177958067", turbo ? 874188075 : 264477561);
            string vadPath = Path.Combine(models, "silero_vad.onnx");
            ModelFiles.Verify(vadPath, "2623a2953f6ff3d2c1e61740c6cdb7168133479b267dfef114a4a3cc5bdd788f", 2327524);
            if (stop.IsCancellationRequested) return;
            using var vad = new SileroVad(vadPath);
            using var mt = new MarianProbe(Path.Combine(models, "marian"));
            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan];
            using var factory = WhisperFactory.FromPath(whisper, new WhisperFactoryOptions { UseGpu = true });

            if (RuntimeOptions.LoadedLibrary != RuntimeLibrary.Vulkan) throw new InvalidOperationException("Vulkan jest niedostępny.");
            async Task<string> Recognize(float[] pcm, CancellationToken token = default)
            {
                await using var processor = factory.CreateBuilder().WithLanguage("en").WithNoContext().WithThreads(4).Build();
                var words = new List<string>(); await foreach (var part in processor.ProcessAsync(pcm, token)) words.Add(part.Text.Trim());
                return string.Join(" ", words);
            }
            if (stop.IsCancellationRequested) return;
            status("Rozgrzewanie modeli… Źródła audio są wyłączone.");
            await Recognize(new float[16000]); await Recognize(new float[16000]); mt.Translate("Good morning."); vad.Process(new float[512]); vad.Reset();
            if (stop.IsCancellationRequested) return;
            clock.Start();
            var worker = Task.Run(async () =>
            {
                try
                {
                    while (!commits.Finished)
                    {
                        var request = commits.Take(clock.Elapsed.TotalMilliseconds);
                        bool preview = request is null;
                        request ??= drafts.Take();
                        if (request is null) { await Task.Delay(10); continue; }
                        if (preview && (!drafts.IsOpen(request.Id) || stop.IsCancellationRequested)) continue;
                        using var previewStop = preview ? CancellationTokenSource.CreateLinkedTokenSource(stop.Token) : null;
                        if (previewStop is not null)
                        {
                            previewStop.CancelAfter(turbo ? 5000 : 3000);
                            lock (previewGate) { previewCancellation = previewStop; if (!drafts.IsOpen(request.Id)) previewStop.Cancel(); }
                        }
                        try
                        {
                            var en = await Recognize(request.Audio, previewStop?.Token ?? CancellationToken.None);
                            if (Fault is not null) break;
                            if (preview && (!drafts.IsOpen(request.Id) || stop.IsCancellationRequested)) continue;
                            var parts = SpeechTranslationInput.Split(en); string pl; string resultStatus = SpeechTranslationInput.Status(parts);
                            try
                            {
                                if (parts.Count > 16 || parts.Any(p => !p.IsLiteral && mt.Encode(p.Text).Length > 512)) throw new InvalidDataException("Limit tłumaczenia");
                                var translations = new List<string>();
                                foreach (var part in parts)
                                {
                                    var translated = part.IsLiteral ? SpeechTranslationInput.Literal(part) : mt.Translate(part.Text, previewStop?.Token ?? CancellationToken.None).Text;
                                    if (string.IsNullOrWhiteSpace(translated)) throw new InvalidDataException("Puste tłumaczenie");
                                    translations.Add(translated);
                                }
                                pl = translations.Count == 0 ? "[nie rozpoznano mowy]" : string.Join(" ", translations);
                            }
                            catch (OperationCanceledException) when (preview) { throw; }
                            catch { pl = "[Nie udało się przetłumaczyć — zachowano tekst angielski]"; resultStatus = "translation-error"; }
                            var result = new Caption(request.Id, en, pl, resultStatus, (clock.Elapsed.TotalMilliseconds - request.At) / 1000, request.StartMs, request.EndMs);
                            if (preview)
                            {
                                if (!stop.IsCancellationRequested) commits.Publish(() => drafts.Publish(request.Id, () => draft?.Invoke(result)));
                            }
                            else commits.Publish(() => { caption(result); Interlocked.Increment(ref published); });
                        }
                        catch (OperationCanceledException) when (preview) { }
                        finally { lock (previewGate) previewCancellation = null; }

                    }
                }
                catch (Exception ex) { Fail("Błąd rozpoznawania: " + ex.Message); }
            });
            var producer = Task.Run(async () =>
            {
                var segmenter = new ReplaySegmenter(); var ring = new SampleRing(336000); var frame = new float[512]; int fill = 0;
                long lastPreviewEnd = 0, samples = 0; double lastAudioAt = 0, lastPacketAt = 0;
                void Commit(ReplayUtterance? u)
                {
                    if (u is null) return;
                    drafts.CloseThrough(++submitted);
                    lock (previewGate) previewCancellation?.Cancel();
                    lastPreviewEnd = u.EndSample;
                    if (!commits.Enqueue(new(submitted, ring.Slice(u.StartSample, u.EndSample), clock.Elapsed.TotalMilliseconds, Math.Max(0, lastAudioAt - (samples - u.StartSample) / 16d), Math.Max(0, lastAudioAt - (samples - u.EndSample) / 16d)), clock.Elapsed.TotalMilliseconds))
                        Fail("Przeciążenie: zatrzymano źródło. Uruchom ponownie szybszy profil.");
                }
                void FlushPartialFrame()
                {
                    if (fill == 0) return;
                    Array.Clear(frame, fill, 512 - fill); ring.Append(frame.AsSpan(0, fill)); samples += fill; lastAudioAt = lastPacketAt;
                    Commit(segmenter.Push(vad.Process(frame), fill)); fill = 0;
                }
                try
                {
                    await foreach (var packet in audio.Reader.ReadAllAsync())
                    {
                        Interlocked.Add(ref queuedBytes, -packet.Bytes.Length);
                        if (Fault is not null) break;
                        if (packet.Bytes.Length == 0) { FlushPartialFrame(); Commit(segmenter.Flush()); vad.Reset(); continue; }
                        lastPacketAt = packet.At;
                        if (clock.Elapsed.TotalMilliseconds - packet.At > 2000) { Fail("Przeciążenie odbioru audio — zatrzymano źródło."); break; }
                        for (int i = 0; i < packet.Bytes.Length; i += 2)
                        {
                            frame[fill++] = BitConverter.ToInt16(packet.Bytes, i) / 32768f;
                            if (fill == 512)
                            {
                                ring.Append(frame); samples += 512; lastAudioAt = packet.At - (packet.Bytes.Length - i - 2) / 32d; Commit(segmenter.Push(vad.Process(frame))); fill = 0;
                                var snapshot = draft is null ? null : segmenter.Snapshot();
                                if (snapshot is not null && snapshot.EndSample - snapshot.StartSample >= 24000 && snapshot.EndSample - lastPreviewEnd >= 24000 && !stop.IsCancellationRequested)
                                {
                                    lastPreviewEnd = snapshot.EndSample;
                                    drafts.Offer(submitted + 1, new(submitted + 1, ring.Slice(snapshot.StartSample, snapshot.EndSample), clock.Elapsed.TotalMilliseconds, Math.Max(0, lastAudioAt - (samples - snapshot.StartSample) / 16d), lastAudioAt));
                                }
                            }
                        }
                    }
                    if (Fault is null)
                    {
                        FlushPartialFrame();
                        Commit(segmenter.Flush());
                    }
                }
                catch (Exception ex) { Fail("Błąd przetwarzania audio: " + ex.Message); }
                finally { commits.Complete(); }
            });
            var monitor = Task.Run(async () =>
            {
                while (!worker.IsCompleted || !producer.IsCompleted)
                {
                    commits.CheckAge(clock.Elapsed.TotalMilliseconds);
                    if (commits.Fault is not null && Fault is null) Fail("Przeciążenie: wynik czekał ponad 2 sekundy. Zatrzymano źródło.");
                    await Task.Delay(25);
                }
            });
            try
            {
                if (wavPath is not null) await FeedFile(wavPath);
                else await EndpointCapture.Run(deviceId ?? throw new InvalidOperationException("Wybierz urządzenie audio."), systemAudio, Accept, () => { if (!audio.Writer.TryWrite(new([], clock.Elapsed.TotalMilliseconds))) Fail("Przeciążenie bufora audio."); },
                    () => status(systemAudio ? "● Dźwięk komputera — nasłuchiwanie" : "● Mikrofon włączony — nasłuchiwanie"), stop.Token,
                    () => status("Urządzenie zmieniło stan. Ponawiam połączenie…"));
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception ex) { Fail("Błąd źródła audio: " + CaptureStartupRecovery.Describe(ex, systemAudio)); }
            finally { audio.Writer.TryComplete(); status(Fault ?? "Źródło wyłączone. Kończę ostatnią frazę…"); }
            await producer; await worker; await monitor;
        }
        catch (Exception ex) { Fail("Nie można uruchomić sesji: " + ex.Message); }
        finally { level(0); status(Fault ?? "Zatrzymano. Źródła audio są wyłączone."); stop.Dispose(); }
    }
    double lastLevel;
    void Accept(byte[] bytes, int count)
    {
        if (count == 0 || Fault is not null) return;
        if (count % 2 != 0) { Fail("Nieprawidłowy format audio."); return; }
        if (Interlocked.Add(ref queuedBytes, count) > 64000) { Fail("Bufor audio jest pełny — zatrzymano źródło."); return; }
        if (!audio.Writer.TryWrite(new(bytes.AsSpan(0, count).ToArray(), clock.Elapsed.TotalMilliseconds))) { Fail("Przeciążenie bufora audio."); return; }
        if (clock.Elapsed.TotalMilliseconds - lastLevel >= 100)
        {
            double peak = 0; for (int i = 0; i < count; i += 2) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(bytes, i) / 32768d));
            lastLevel = clock.Elapsed.TotalMilliseconds; level(Math.Min(1, peak * 3));
        }
    }
    async Task FeedFile(string path)
    {
        using var reader = new WaveFileReader(path);
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16 || reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm)
            throw new InvalidDataException("Wybierz WAV PCM16, mono, 16 kHz.");
        status("▶ Test z pliku — urządzenia audio są wyłączone");
        var bytes = new byte[1024]; var pacing = Stopwatch.StartNew(); long total = 0; int count;
        while (!stop.IsCancellationRequested && (count = reader.Read(bytes, 0, bytes.Length)) > 0)
        {
            total += count; double wait = total / 32d - pacing.Elapsed.TotalMilliseconds;
            if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), stop.Token);
            if (pacing.Elapsed.TotalMilliseconds - total / 32d > 2000) throw new InvalidOperationException("Test z pliku nie nadąża za czasem nagrania.");
            Accept(bytes, count);
        }
    }
}
