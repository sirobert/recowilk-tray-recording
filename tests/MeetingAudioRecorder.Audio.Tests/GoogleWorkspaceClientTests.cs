using System.Net;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Infrastructure.Google;

namespace MeetingAudioRecorder.Audio.Tests;

public sealed class GoogleWorkspaceClientTests
{
    [Fact]
    public async Task Calendar_ReturnsOnlyAcceptedTimedGoogleMeetEvents()
    {
        const string response = """
            {
              "items": [
                {
                  "id": "accepted",
                  "status": "confirmed",
                  "summary": "Daily",
                  "start": { "dateTime": "2026-08-08T10:00:00+02:00" },
                  "end": { "dateTime": "2026-08-08T10:30:00+02:00" },
                  "hangoutLink": "https://meet.google.com/abc-defg-hij?authuser=0",
                  "attendees": [{ "self": true, "responseStatus": "accepted" }]
                },
                {
                  "id": "declined",
                  "status": "confirmed",
                  "summary": "Declined",
                  "start": { "dateTime": "2026-08-08T11:00:00+02:00" },
                  "end": { "dateTime": "2026-08-08T11:30:00+02:00" },
                  "hangoutLink": "https://meet.google.com/bcd-efgh-ijk",
                  "attendees": [{ "self": true, "responseStatus": "declined" }]
                },
                {
                  "id": "cancelled",
                  "status": "cancelled",
                  "start": { "dateTime": "2026-08-08T12:00:00+02:00" },
                  "end": { "dateTime": "2026-08-08T12:30:00+02:00" },
                  "hangoutLink": "https://meet.google.com/cde-fghi-jkl"
                },
                {
                  "id": "all-day",
                  "status": "confirmed",
                  "start": { "date": "2026-08-08" },
                  "end": { "date": "2026-08-09" },
                  "hangoutLink": "https://meet.google.com/def-ghij-klm"
                }
              ]
            }
            """;
        var handler = new RecordingHttpMessageHandler(_ => Json(response));
        var client = new GoogleCalendarClient(new HttpClient(handler), new FixedTokenProvider());

        var meetings = await client.ListMeetingCandidatesAsync(
            new DateTimeOffset(2026, 8, 8, 7, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));

        var meeting = Assert.Single(meetings);
        Assert.Equal("accepted", meeting.EventId);
        Assert.Equal("Daily", meeting.Title);
        Assert.Equal("abc-defg-hij", meeting.MeetingCode);
        Assert.Equal("https://meet.google.com/abc-defg-hij?authuser=0", meeting.MeetingUri);
        Assert.Equal("Bearer", handler.Requests[0].AuthorizationScheme);
        Assert.Equal("test-access-token", handler.Requests[0].AuthorizationParameter);
        Assert.Contains("singleEvents=true", handler.Requests[0].RequestUri!.Query, StringComparison.Ordinal);
        Assert.Contains("maxAttendees=1", handler.Requests[0].RequestUri!.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("description", handler.Requests[0].RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Calendar_FollowsPageTokenAndUsesConferenceVideoEntryPoint()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            if (!request.RequestUri!.Query.Contains("pageToken=page-2", StringComparison.Ordinal))
            {
                return Json("""
                    { "items": [], "nextPageToken": "page-2" }
                    """);
            }

            return Json("""
                {
                  "items": [{
                    "id": "page-two-event",
                    "status": "confirmed",
                    "summary": "Planning",
                    "start": { "dateTime": "2026-08-08T10:00:00Z" },
                    "end": { "dateTime": "2026-08-08T11:00:00Z" },
                    "conferenceData": {
                      "entryPoints": [
                        { "entryPointType": "phone", "uri": "tel:+48123456789" },
                        { "entryPointType": "video", "uri": "https://meet.google.com/xyz-abcd-efg" }
                      ]
                    }
                  }]
                }
                """);
        });
        var client = new GoogleCalendarClient(new HttpClient(handler), new FixedTokenProvider());

        var meetings = await client.ListMeetingCandidatesAsync(
            DateTimeOffset.Parse("2026-08-08T09:00:00Z"),
            DateTimeOffset.Parse("2026-08-08T12:00:00Z"));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("xyz-abcd-efg", Assert.Single(meetings).MeetingCode);
    }

    [Fact]
    public async Task Meet_ReturnsPresentWhenAuthenticatedUserIsOnAnyActivePage()
    {
        var handler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/spaces/abc-defg-hij", StringComparison.Ordinal))
            {
                return Json("""
                    { "activeConference": { "conferenceRecord": "conferenceRecords/conference-123" } }
                    """);
            }

            if (!request.RequestUri.Query.Contains("pageToken=page-2", StringComparison.Ordinal))
            {
                return Json("""
                    {
                      "participants": [{ "signedInUser": { "user": "users/someone-else" } }],
                      "nextPageToken": "page-2"
                    }
                    """);
            }

            return Json("""
                { "participants": [{ "signedInUser": { "user": "users/me-123" } }] }
                """);
        });
        var client = new GoogleMeetClient(new HttpClient(handler), new FixedTokenProvider());

        var presence = await client.GetCurrentUserPresenceAsync("ABC-DEFG-HIJ", "users/me-123");

        Assert.Equal(MeetingPresenceStatus.Present, presence.Status);
        Assert.Equal("conferenceRecords/conference-123", presence.ConferenceRecordName);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(
            "latest_end_time IS NULL",
            Uri.UnescapeDataString(handler.Requests[1].RequestUri!.Query),
            StringComparison.Ordinal);
        Assert.Contains("fields=", handler.Requests[1].RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Meet_WithoutActiveConferenceReturnsAbsentWithoutListingParticipants()
    {
        var handler = new RecordingHttpMessageHandler(_ => Json("{ \"name\": \"spaces/space-1\" }"));
        var client = new GoogleMeetClient(new HttpClient(handler), new FixedTokenProvider());

        var presence = await client.GetCurrentUserPresenceAsync("abc-defg-hij", "users/me-123");

        Assert.Equal(MeetingPresenceStatus.Absent, presence.Status);
        Assert.Null(presence.ConferenceRecordName);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Meet_RejectsInvalidMeetingCodeBeforeNetworkCall()
    {
        var handler = new RecordingHttpMessageHandler(_ => throw new InvalidOperationException("No request expected."));
        var client = new GoogleMeetClient(new HttpClient(handler), new FixedTokenProvider());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetCurrentUserPresenceAsync("https://evil.example/meeting", "users/me-123"));

        Assert.Empty(handler.Requests);
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class FixedTokenProvider : IGoogleAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("test-access-token");
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        string? AuthorizationScheme,
        string? AuthorizationParameter);
}
