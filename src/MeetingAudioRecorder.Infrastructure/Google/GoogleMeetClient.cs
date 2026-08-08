using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Infrastructure.Google;

public sealed class GoogleMeetClient : IGoogleMeetClient
{
    private const string MeetEndpoint = "https://meet.googleapis.com/v2/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IGoogleAccessTokenProvider _tokenProvider;

    public GoogleMeetClient(HttpClient httpClient, IGoogleAccessTokenProvider tokenProvider)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
    }

    public async Task<GoogleMeetPresence> GetCurrentUserPresenceAsync(
        string meetingCode,
        string accountUserId,
        CancellationToken cancellationToken = default)
    {
        if (!GoogleMeetCode.TryExtract(meetingCode, out var normalizedMeetingCode))
            throw new ArgumentException("Nieprawidłowy kod spotkania Google Meet.", nameof(meetingCode));

        var normalizedUserId = NormalizeUserId(accountUserId);
        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Brak tokenu dostępu Google.");

        var space = await GetSpaceAsync(normalizedMeetingCode, accessToken, cancellationToken).ConfigureAwait(false);
        var conferenceRecordName = space?.ActiveConference?.ConferenceRecord;
        if (string.IsNullOrWhiteSpace(conferenceRecordName))
            return new GoogleMeetPresence(normalizedMeetingCode, null, MeetingPresenceStatus.Absent);

        ValidateConferenceRecordName(conferenceRecordName);
        var isPresent = await FindActiveUserAsync(
            conferenceRecordName,
            normalizedUserId,
            accessToken,
            cancellationToken).ConfigureAwait(false);

        return new GoogleMeetPresence(
            normalizedMeetingCode,
            conferenceRecordName,
            isPresent ? MeetingPresenceStatus.Present : MeetingPresenceStatus.Absent);
    }

    private async Task<MeetSpace?> GetSpaceAsync(
        string meetingCode,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            MeetEndpoint + "spaces/" + Uri.EscapeDataString(meetingCode)
            + "?fields=" + Uri.EscapeDataString("activeConference/conferenceRecord"),
            UriKind.Absolute);
        using var request = CreateAuthorizedRequest(uri, accessToken);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<MeetSpace>(
            stream,
            JsonOptions,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> FindActiveUserAsync(
        string conferenceRecordName,
        string accountUserId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var seenPageTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;

        do
        {
            var parameters = new List<KeyValuePair<string, string>>
            {
                new("filter", "latest_end_time IS NULL"),
                new("pageSize", "250"),
                new("fields", "participants(signedinUser/user),nextPageToken")
            };
            if (pageToken is not null)
                parameters.Add(new("pageToken", pageToken));

            var query = string.Join(
                "&",
                parameters.Select(pair =>
                    Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
            var uri = new Uri(
                MeetEndpoint + conferenceRecordName + "/participants?" + query,
                UriKind.Absolute);
            using var request = CreateAuthorizedRequest(uri, accessToken);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<ParticipantsResponse>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (page?.Participants?.Any(participant =>
                    string.Equals(participant.SignedInUser?.User, accountUserId, StringComparison.Ordinal)) == true)
            {
                return true;
            }

            pageToken = string.IsNullOrWhiteSpace(page?.NextPageToken) ? null : page.NextPageToken;
            if (pageToken is not null && !seenPageTokens.Add(pageToken))
                throw new InvalidDataException("Google Meet zwrócił powtarzający się token strony.");
        }
        while (pageToken is not null);

        return false;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(Uri uri, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private static string NormalizeUserId(string accountUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountUserId);
        var value = accountUserId.Trim();
        if (value.StartsWith("people/", StringComparison.Ordinal))
            value = "users/" + value["people/".Length..];
        else if (!value.StartsWith("users/", StringComparison.Ordinal))
            value = "users/" + value;

        if (value.Length <= "users/".Length || value.IndexOf('/', "users/".Length) >= 0)
            throw new ArgumentException("Nieprawidłowy identyfikator konta Google.", nameof(accountUserId));

        return value;
    }

    private static void ValidateConferenceRecordName(string value)
    {
        const string prefix = "conferenceRecords/";
        if (!value.StartsWith(prefix, StringComparison.Ordinal)
            || value.Length == prefix.Length
            || value.IndexOf('/', prefix.Length) >= 0)
        {
            throw new InvalidDataException("Google Meet zwrócił nieprawidłową nazwę konferencji.");
        }
    }

    private sealed class MeetSpace
    {
        public ActiveConference? ActiveConference { get; init; }
    }

    private sealed class ActiveConference
    {
        public string? ConferenceRecord { get; init; }
    }

    private sealed class ParticipantsResponse
    {
        public List<Participant>? Participants { get; init; }
        public string? NextPageToken { get; init; }
    }

    private sealed class Participant
    {
        [JsonPropertyName("signedinUser")]
        public SignedInUser? SignedInUser { get; init; }
    }

    private sealed class SignedInUser
    {
        public string? User { get; init; }
    }
}
