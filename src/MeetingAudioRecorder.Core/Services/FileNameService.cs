using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MeetingAudioRecorder.Core.Interfaces;

namespace MeetingAudioRecorder.Core.Services;

public sealed class FileNameService : IFileNameService
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    public string GenerateFileName(string format, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(format))
            format = "Nagranie_yyyy-MM-dd_HH-mm-ss.mp3";

        // Obsługa formatu z rozszerzeniem .mp3 i wzorcami daty
        var name = format;
        var hasExtension = name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
        if (hasExtension)
            name = name[..^4];

        // Zamień standardowe tokeny DateTime
        name = ReplaceDateTokens(name, timestamp.LocalDateTime);

        // Usuń niedozwolone znaki
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (Array.IndexOf(InvalidChars, ch) >= 0)
                sb.Append('_');
            else
                sb.Append(ch);
        }

        var cleaned = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "Nagranie_" + timestamp.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);

        return cleaned + ".mp3";
    }

    public string EnsureUniquePath(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            return path;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);

        for (var i = 2; i < 10_000; i++)
        {
            var candidate = Path.Combine(directory, $"{nameWithoutExt}_{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{nameWithoutExt}_{Guid.NewGuid():N}{ext}");
    }

    public bool IsValidFileNameFormat(string format, out string? error)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            error = "Format nazwy pliku jest pusty.";
            return false;
        }

        var sample = GenerateFileName(format, DateTimeOffset.Now);
        if (sample.IndexOfAny(InvalidChars) >= 0)
        {
            error = "Format generuje niedozwolone znaki w nazwie pliku.";
            return false;
        }

        error = null;
        return true;
    }

    private static string ReplaceDateTokens(string format, DateTime dt)
    {
        // Obsługa wzorców typu yyyy-MM-dd_HH-mm-ss w środku nazwy
        // Najpierw spróbuj pełnego formatu DateTime, potem ręczne zamiany popularnych tokenów
        try
        {
            // Jeśli format jest czystym formatem DateTime (z prefiksem tekstowym oddzielonym),
            // używamy Regex do zamiany sekwencji tokenów.
            var pattern = new Regex(
                @"(yyyy|yy|MM|dd|HH|mm|ss|fff)+([-_\.]?(yyyy|yy|MM|dd|HH|mm|ss|fff))*",
                RegexOptions.Compiled);

            return pattern.Replace(format, m =>
            {
                try
                {
                    return dt.ToString(m.Value, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return m.Value;
                }
            });
        }
        catch
        {
            return format
                .Replace("yyyy", dt.ToString("yyyy", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("MM", dt.ToString("MM", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("dd", dt.ToString("dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("HH", dt.ToString("HH", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("mm", dt.ToString("mm", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("ss", dt.ToString("ss", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }
}
