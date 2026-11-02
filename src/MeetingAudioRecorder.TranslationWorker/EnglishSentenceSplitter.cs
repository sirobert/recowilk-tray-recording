using System.Text.RegularExpressions;

namespace MeetingAudioRecorder.TranslationWorker;

public sealed record SentencePart(int Start, int Length, string Text);

/// <summary>Conservative English sentence boundaries for finalized ASR, never partial text.</summary>
public static partial class EnglishSentenceSplitter
{
    static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        { "mr", "mrs", "ms", "dr", "prof", "sr", "jr", "st", "vs", "etc", "e.g", "i.e" };

    public static IReadOnlyList<SentencePart> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = new List<SentencePart>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?')) continue;
            if (text[i] == '.' && IsAbbreviation(text, i)) continue;
            int end = i + 1;
            while (end < text.Length && text[end] is '.' or '!' or '?' or '"' or '\'' or '”' or '’' or ')' or ']') end++;
            // In particular, don't split decimals, URLs or email addresses.
            if (end < text.Length && !char.IsWhiteSpace(text[end])) continue;
            AddPart(start, end);
            start = end;
            i = end - 1;
        }
        AddPart(start, text.Length);
        return parts;

        void AddPart(int from, int to)
        {
            while (from < to && char.IsWhiteSpace(text[from])) from++;
            while (to > from && char.IsWhiteSpace(text[to - 1])) to--;
            if (from < to) parts.Add(new(from, to - from, text[from..to]));
        }
    }

    static bool IsAbbreviation(string text, int dot)
    {
        int start = dot;
        while (start > 0 && (char.IsLetter(text[start - 1]) || text[start - 1] == '.')) start--;
        var word = text[start..dot];
        return Abbreviations.Contains(word)
            || (word.Length == 1 && char.IsUpper(word[0]))
            || Acronym().IsMatch(text[start..(dot + 1)]);
    }

    [GeneratedRegex(@"^(?:[A-Za-z]\.){2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex Acronym();
}
