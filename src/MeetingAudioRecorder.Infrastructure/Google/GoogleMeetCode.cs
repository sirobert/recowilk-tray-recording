using System.Text.RegularExpressions;

namespace MeetingAudioRecorder.Infrastructure.Google;

internal static partial class GoogleMeetCode
{
    public static bool TryExtract(string? value, out string meetingCode)
    {
        meetingCode = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, "meet.google.com", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            candidate = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                        ?? string.Empty;
        }

        candidate = candidate.Trim().ToLowerInvariant();
        if (candidate.Length is 0 or > 128 || !MeetingCodePattern().IsMatch(candidate))
            return false;

        meetingCode = candidate;
        return true;
    }

    [GeneratedRegex("^[a-z]+-[a-z]+-[a-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex MeetingCodePattern();
}
