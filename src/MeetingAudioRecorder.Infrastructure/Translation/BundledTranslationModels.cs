using System.Text.Json;

namespace MeetingAudioRecorder.Infrastructure.Translation;

public static class BundledTranslationModels
{
    public static IReadOnlyList<ModelDownload> Files()
    {
        using var stream = typeof(BundledTranslationModels).Assembly.GetManifestResourceStream("TranslationMarianPins")!;
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.EnumerateObject().Select(p => new ModelDownload(
            Path.Combine("marian", p.Name), new Uri(Path.Combine(AppContext.BaseDirectory, "TranslationModels", "marian", p.Name)),
            p.Value.GetProperty("bytes").GetInt64(), p.Value.GetProperty("sha256").GetString()!)).ToArray();
    }
    public static async Task InstallAsync(string directory, CancellationToken token)
    {
        foreach (var file in Files())
        {
            var target = Path.Combine(directory, file.Path);
            if (await TranslationModelDownloader.IsValidAsync(target, file, token)) continue;
            if (!await TranslationModelDownloader.IsValidAsync(file.Url.LocalPath, file, token))
                throw new InvalidDataException("Brak poprawnego pakietu Marian. Zainstaluj ponownie pełny pakiet Recordera.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var partial = target + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                await using (var source = File.OpenRead(file.Url.LocalPath))
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                    await source.CopyToAsync(output, token);
                if (!await TranslationModelDownloader.IsValidAsync(partial, file, token)) throw new InvalidDataException("Błąd kopiowania modelu Marian.");
                token.ThrowIfCancellationRequested(); File.Move(partial, target, true);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
    }
    public static async Task<bool> IsReadyAsync(string directory, bool turbo, CancellationToken token)
    {
        foreach (var file in Files().Concat(TranslationModelDownloader.ForProfile(turbo)))
            if (!await TranslationModelDownloader.IsValidAsync(Path.Combine(directory, file.Path), file, token)) return false;
        return true;
    }
}
