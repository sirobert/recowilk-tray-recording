using System.Text.RegularExpressions;

namespace MeetingAudioRecorder.TranslationWorker;

public static class SpeechTranslationInput
{
    // Narrow allowlist: ordinary bracketed speech must not be discarded.
    static readonly Regex Marker = new(@"\[\s*(?:inaudible|unintelligible|blank_audio)\s*\]|\(\s*audience member speaking off microphone\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public sealed record Part(int Start, int Length, string Text, bool IsUnrecognized, bool IsLiteral);

    public static IReadOnlyList<Part> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<Part>();
        int position = 0;
        void AddText(int start, int length)
        {
            foreach (var part in EnglishSentenceSplitter.Split(text.Substring(start, length)))
                parts.Add(new(start + part.Start, part.Length, part.Text, false, !part.Text.Any(char.IsLetterOrDigit)));
        }
        foreach (Match match in Marker.Matches(text))
        {
            AddText(position, match.Index - position);
            parts.Add(new(match.Index, match.Length, match.Value, true, true));
            position = match.Index + match.Length;
        }
        AddText(position, text.Length - position);
        return parts;
    }

    public static string Status(IReadOnlyList<Part> parts) =>
        parts.Any(p => p.IsUnrecognized)
            ? parts.Any(p => !p.IsLiteral) ? "partial-recognition" : "unrecognized"
            : parts.Any(p => !p.IsLiteral) ? "text" : "no-speech-text";

    public static string Literal(Part part) => part.IsUnrecognized ? "[nie rozpoznano mowy]" : part.Text;
}
