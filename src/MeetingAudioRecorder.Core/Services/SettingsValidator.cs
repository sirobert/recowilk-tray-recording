using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Services;

public static class SettingsValidator
{
    private static readonly int[] AllowedBitrates = [128, 192, 256, 320];
    private static readonly int[] AllowedSampleRates = [44100, 48000];

    public static ValidationResult Validate(AppSettings settings)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (settings.Mp3BitrateKbps is not (128 or 192 or 256 or 320))
            errors.Add($"Nieobsługiwany bitrate MP3: {settings.Mp3BitrateKbps}. Dozwolone: 128, 192, 256, 320.");

        if (!AllowedSampleRates.Contains(settings.TargetSampleRate))
            errors.Add($"Nieobsługiwana częstotliwość próbkowania: {settings.TargetSampleRate}. Dozwolone: 44100, 48000.");

        if (settings.MicrophoneVolume is < 0 or > 2.0)
            errors.Add("Poziom głośności mikrofonu musi być w zakresie 0.0–2.0.");

        if (settings.LoopbackVolume is < 0 or > 2.0)
            errors.Add("Poziom głośności dźwięku wyjściowego musi być w zakresie 0.0–2.0.");

        if (string.IsNullOrWhiteSpace(settings.RecordingsDirectory))
            errors.Add("Folder zapisu nagrań nie może być pusty.");
        else
        {
            try
            {
                var full = Path.GetFullPath(settings.RecordingsDirectory);
                if (full.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    errors.Add("Folder zapisu zawiera niedozwolone znaki.");
            }
            catch (Exception ex)
            {
                errors.Add($"Nieprawidłowa ścieżka folderu zapisu: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(settings.FileNameFormat))
            errors.Add("Format nazwy pliku nie może być pusty.");
        else if (!settings.FileNameFormat.Contains("yyyy", StringComparison.Ordinal)
                 && !settings.FileNameFormat.Contains("HH", StringComparison.Ordinal))
            warnings.Add("Format nazwy pliku nie zawiera znacznika czasu — pliki mogą kolidować.");

        if (settings.Hotkey is null)
            errors.Add("Brak konfiguracji skrótu klawiszowego.");
        else
        {
            if (string.IsNullOrWhiteSpace(settings.Hotkey.Key))
                errors.Add("Klawisz skrótu nie może być pusty.");
            if (!settings.Hotkey.Control && !settings.Hotkey.Alt && !settings.Hotkey.Shift && !settings.Hotkey.Windows)
                errors.Add("Skrót klawiszowy musi zawierać co najmniej jeden modyfikator (Ctrl, Alt, Shift lub Win).");
        }

        if (settings.RecowilkUploadEnabled)
        {
            if (!Uri.TryCreate(settings.RecowilkBaseUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps
                    && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
                errors.Add("Adres RecoWilk musi być adresem HTTPS (HTTP jest dozwolone tylko dla localhost).");
        }

        if (errors.Count > 0)
            return ValidationResult.Failure(errors.ToArray());

        return ValidationResult.Success(warnings.ToArray());
    }

    public static AppSettings Sanitize(AppSettings settings)
    {
        var result = settings.Clone();

        if (!AllowedBitrates.Contains(result.Mp3BitrateKbps))
            result.Mp3BitrateKbps = 192;

        if (!AllowedSampleRates.Contains(result.TargetSampleRate))
            result.TargetSampleRate = 48000;

        result.MicrophoneVolume = Math.Clamp(result.MicrophoneVolume, 0, 2.0);
        result.LoopbackVolume = Math.Clamp(result.LoopbackVolume, 0, 2.0);

        if (string.IsNullOrWhiteSpace(result.RecordingsDirectory))
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            result.RecordingsDirectory = Path.Combine(documents, "Nagrania spotkań");
        }

        if (string.IsNullOrWhiteSpace(result.FileNameFormat))
            result.FileNameFormat = "Nagranie_yyyy-MM-dd_HH-mm-ss.mp3";

        result.Hotkey ??= new HotkeySettings();
        result.RecowilkBaseUrl = result.RecowilkBaseUrl?.Trim().TrimEnd('/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(result.Hotkey.Key))
            result.Hotkey.Key = "R";

        if (!result.Hotkey.Control && !result.Hotkey.Alt && !result.Hotkey.Shift && !result.Hotkey.Windows)
        {
            result.Hotkey.Control = true;
            result.Hotkey.Alt = true;
        }

        return result;
    }
}
