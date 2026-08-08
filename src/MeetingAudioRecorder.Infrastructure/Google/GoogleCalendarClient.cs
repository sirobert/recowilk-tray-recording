using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Infrastructure.Google;

public sealed class GoogleCalendarClient : IGoogleCalendarClient
{
    private const string EventsEndpoint =
        "https://www.googleapis.com/calendar/v3/calendars/primary/events";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IGoogleAccessTokenProvider _tokenProvider;

    public GoogleCalendarClient(HttpClient httpClient, IGoogleAccessTokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
    }

    public async Task<IReadOnlyList<GoogleCalendarMeeting>> ListMeetingCandidatesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
            throw new ArgumentOutOfRangeException(nameof(to), "Koniec zakresu musi być późniejszy niż początek.");

        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Brak tokenu dostępu Google.");

        var meetings = new List<GoogleCalendarMeeting>();
        var seenPageTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;

        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildEventsUri(from, to, pageToken));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<CalendarEventsResponse>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (page?.Items is not null)
            {
                foreach (var item in page.Items)
                {
                    if (TryMapMeeting(item, out var meeting))
                        meetings.Add(meeting);
                }
            }

            pageToken = string.IsNullOrWhiteSpace(page?.NextPageToken) ? null : page.NextPageToken;
            if (pageToken is not null && !seenPageTokens.Add(pageToken))
                throw new InvalidDataException("Google Calendar zwrócił powtarzający się token strony.");
        }
        while (pageToken is not null);

        return meetings;
    }

    private static Uri BuildEventsUri(DateTimeOffset from, DateTimeOffset to, string? pageToken)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("singleEvents", "true"),
            new("showDeleted", "false"),
            new("orderBy", "startTime"),
            new("maxResults", "50"),
            new("maxAttendees", "1"),
            new("timeMin", from.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            new("timeMax", to.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
            new("fields", "nextPageToken,items(id,status,summary,start/dateTime,end/dateTime,hangoutLink,conferenceData/entryPoints,attendees(self,responseStatus))")
        };
        if (pageToken is not null)
            parameters.Add(new("pageToken", pageToken));

        var query = string.Join(
            "&",
            parameters.Select(pair =>
                Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        return new Uri(EventsEndpoint + "?" + query, UriKind.Absolute);
    }

    private static bool TryMapMeeting(CalendarEvent item, out GoogleCalendarMeeting meeting)
    {
        meeting = null!;
        if (string.IsNullOrWhiteSpace(item.Id)
            || string.Equals(item.Status, "cancelled", StringComparison.OrdinalIgnoreCase)
            || IsDeclined(item.Attendees)
            || !TryParseDateTime(item.Start?.DateTime, out var startsAt)
            || !TryParseDateTime(item.End?.DateTime, out var endsAt)
            || endsAt <= startsAt)
        {
            return false;
        }

        var meetingUri = FindMeetingUri(item);
        if (!GoogleMeetCode.TryExtract(meetingUri, out var meetingCode))
            return false;

        meeting = new GoogleCalendarMeeting(
            item.Id,
            string.IsNullOrWhiteSpace(item.Summary) ? "Spotkanie Google Meet" : item.Summary.Trim(),
            startsAt,
            endsAt,
            meetingUri!,
            meetingCode);
        return true;
    }

    private static bool IsDeclined(IReadOnlyList<CalendarAttendee>? attendees)
        => attendees?.Any(attendee =>
            attendee.Self
            && string.Equals(attendee.ResponseStatus, "declined", StringComparison.OrdinalIgnoreCase)) == true;

    private static string? FindMeetingUri(CalendarEvent item)
    {
        if (GoogleMeetCode.TryExtract(item.HangoutLink, out _))
            return item.HangoutLink;

        return item.ConferenceData?.EntryPoints?
            .FirstOrDefault(entryPoint =>
                string.Equals(entryPoint.EntryPointType, "video", StringComparison.OrdinalIgnoreCase)
                && GoogleMeetCode.TryExtract(entryPoint.Uri, out _))
            ?.Uri;
    }

    private static bool TryParseDateTime(string? value, out DateTimeOffset result)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out result);

    private sealed class CalendarEventsResponse
    {
        public List<CalendarEvent>? Items { get; init; }
        public string? NextPageToken { get; init; }
    }

    private sealed class CalendarEvent
    {
        public string? Id { get; init; }
        public string? Status { get; init; }
        public string? Summary { get; init; }
        public CalendarDateTime? Start { get; init; }
        public CalendarDateTime? End { get; init; }
        public string? HangoutLink { get; init; }
        public CalendarConferenceData? ConferenceData { get; init; }
        public List<CalendarAttendee>? Attendees { get; init; }
    }

    private sealed class CalendarDateTime
    {
        [JsonPropertyName("dateTime")]
        public string? DateTime { get; init; }
    }

    private sealed class CalendarConferenceData
    {
        public List<CalendarEntryPoint>? EntryPoints { get; init; }
    }

    private sealed class CalendarEntryPoint
    {
        public string? EntryPointType { get; init; }
        public string? Uri { get; init; }
    }

    private sealed class CalendarAttendee
    {
        public bool Self { get; init; }
        public string? ResponseStatus { get; init; }
    }
}
