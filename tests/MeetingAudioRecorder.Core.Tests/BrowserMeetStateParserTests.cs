using MeetingAudioRecorder.Core.Services;

namespace MeetingAudioRecorder.Core.Tests;

public sealed class BrowserMeetStateParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidFreshState_ReturnsNormalizedDistinctMeetingCodes()
    {
        var json = $$"""
            {
              "version": 1,
              "observedAtUtc": "{{Now:O}}",
              "links": [
                { "meetingCode": "TCU-YSXP-TVW", "browser": "chrome" },
                { "meetingCode": "tcu-ysxp-tvw", "browser": "chrome" },
                { "meetingCode": "not-a-valid-code!", "browser": "chrome" }
              ]
            }
            """;

        var links = BrowserMeetStateParser.ParseFresh(json, Now, TimeSpan.FromSeconds(90));

        var link = Assert.Single(links);
        Assert.Equal("tcu-ysxp-tvw", link.MeetingCode);
        Assert.Equal("chrome", link.Browser);
    }

    [Fact]
    public void StaleOrMalformedState_ReturnsNoLinks()
    {
        var stale = $$"""
            {
              "version": 1,
              "observedAtUtc": "{{Now.AddMinutes(-2):O}}",
              "links": [{ "meetingCode": "tcu-ysxp-tvw", "browser": "chrome" }]
            }
            """;

        Assert.Empty(BrowserMeetStateParser.ParseFresh(stale, Now, TimeSpan.FromSeconds(90)));
        Assert.Empty(BrowserMeetStateParser.ParseFresh("not-json", Now, TimeSpan.FromSeconds(90)));
    }
}
