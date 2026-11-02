using System.Net.Http;
using System.Security.Cryptography;

namespace MeetingAudioRecorder.Infrastructure.Translation;

public sealed record ModelDownload(string Path, Uri Url, long Bytes, string Sha256);
public sealed record ModelDownloadProgress(string File, long Completed, long Total, string Stage);

public sealed class TranslationModelDownloader(HttpClient client)
{
    public static IReadOnlyList<ModelDownload> ForProfile(bool turbo) =>
    [
        new("silero_vad.onnx", new("https://github.com/snakers4/silero-vad/raw/v5.1.2/src/silero_vad/data/silero_vad.onnx"), 2327524, "2623a2953f6ff3d2c1e61740c6cdb7168133479b267dfef114a4a3cc5bdd788f"),
        turbo
            ? new("ggml-large-v3-turbo-q8_0.bin", new("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-large-v3-turbo-q8_0.bin"), 874188075, "317eb69c11673c9de1e1f0d459b253999804ec71ac4c23c17ecf5fbe24e259a1")
            : new("ggml-small.en-q8_0.bin", new("https://huggingface.co/sandrohanea/whisper.net/resolve/3172f1d6a8051229081aeb628e7021e0ee6217a3/q8_0/ggml-small.en.bin"), 264477561, "67a179f608ea6114bd3fdb9060e762b588a3fb3bd00c4387971be4d177958067")
    ];

    public static async Task<bool> IsValidAsync(string target, ModelDownload file, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!File.Exists(target) || new FileInfo(target).Length != file.Bytes) return false;
        await using var stream = File.OpenRead(target);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public async Task EnsureAsync(string directory, IReadOnlyList<ModelDownload> files, IProgress<ModelDownloadProgress>? progress, CancellationToken token)
    {
        long completed = 0, total = files.Sum(f => f.Bytes);
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            var root = Path.GetFullPath(directory);
            if (!Path.EndsInDirectorySeparator(root)) root += Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Path.Combine(root, file.Path));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Niepoprawna ścieżka modelu.");
            progress?.Report(new(file.Path, completed, total, "Sprawdzanie"));
            if (!await IsValidAsync(target, file, token))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var partial = target + "." + Guid.NewGuid().ToString("N") + ".partial";
                try
                {
                    using var response = await client.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is long length && length != file.Bytes) throw new InvalidDataException("Niepoprawny rozmiar modelu: " + file.Path);
                    await using (var input = await response.Content.ReadAsStreamAsync(token))
                    await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
                    {
                        var buffer = new byte[131072]; long received = 0;
                        while (true)
                        {
                            int count = await input.ReadAsync(buffer, token); if (count == 0) break;
                            received += count;
                            if (received > file.Bytes) throw new InvalidDataException("Model przekracza oczekiwany rozmiar.");
                            await output.WriteAsync(buffer.AsMemory(0, count), token);
                            progress?.Report(new(file.Path, completed + received, total, "Pobieranie"));
                        }
                        await output.FlushAsync(token);
                    }
                    progress?.Report(new(file.Path, completed + file.Bytes, total, "Weryfikacja SHA-256"));
                    if (!await IsValidAsync(partial, file, token)) throw new InvalidDataException("Nieprawidłowa suma kontrolna modelu: " + file.Path);
                    token.ThrowIfCancellationRequested();
                    File.Move(partial, target, true);
                }
                finally { if (File.Exists(partial)) File.Delete(partial); }
            }
            completed += file.Bytes; progress?.Report(new(file.Path, completed, total, "Gotowe"));
        }
    }
}
