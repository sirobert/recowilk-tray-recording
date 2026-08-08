using System.Text.Json;
using System.Text.RegularExpressions;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Services;

public static partial class BrowserMeetStateParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<BrowserMeetLink> ParseFresh(
        string json,
        DateTimeOffset now,
        TimeSpan maximumAge)
    {
        if (string.IsNullOrWhiteSpace(json) || maximumAge <= TimeSpan.Zero)
            return [];

        try
        {
            var document = JsonSerializer.Deserialize<StateDocument>(json, SerializerOptions);
            if (document is null
                || document.Version != 1
                || document.ObservedAtUtc > now.AddMinutes(5)
                || now - document.ObservedAtUtc > maximumAge)
            {
                return [];
            }

            return (document.Links ?? [])
                .Select(link => new
                {
                    Code = link.MeetingCode?.Trim().ToLowerInvariant(),
                    Browser = string.IsNullOrWhiteSpace(link.Browser) ? null : link.Browser.Trim()
                })
                .Where(link => link.Code is not null && MeetingCodeRegex().IsMatch(link.Code))
                .DistinctBy(link => link.Code, StringComparer.OrdinalIgnoreCase)
                .Select(link => new BrowserMeetLink(link.Code!, link.Browser, document.ObservedAtUtc))
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    [GeneratedRegex("^(?=.{5,128}$)[a-z]+-[a-z]+-[a-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex MeetingCodeRegex();

    private sealed class StateDocument
    {
        public int Version { get; init; }
        public DateTimeOffset ObservedAtUtc { get; init; }
        public StateLink[]? Links { get; init; }
    }

    private sealed class StateLink
    {
        public string? MeetingCode { get; init; }
        public string? Browser { get; init; }
    }
}
