using MeetingAudioRecorder.Audio.Capture;

namespace MeetingAudioRecorder.Audio.Tests;

public class LoopbackFrameTimelineTests
{
    [Theory]
    [InlineData(44100)]
    [InlineData(48000)]
    public void RegularPackets_ForTenMinutes_KeepExactFrameCount(int sampleRate)
    {
        var timeline = new LoopbackFrameTimeline(sampleRate, gapToleranceFrames: 0);
        var packetFrames = sampleRate / 10;
        var packetDuration = TimeSpan.FromMilliseconds(100);
        long writtenFrames = 0;

        for (var packet = 1; packet <= 6_000; packet++)
        {
            var plan = timeline.PlanPacket(packetDuration.Ticks * packet, packetFrames);
            writtenFrames += plan.SilenceFrames + plan.AudioFrames;
        }

        Assert.Equal(600L * sampleRate, writtenFrames);
        Assert.Equal(writtenFrames, timeline.PositionFrames);
    }

    [Fact]
    public void ConfirmedGap_InsertsOnlyMissingFrames()
    {
        const int sampleRate = 48000;
        var timeline = new LoopbackFrameTimeline(sampleRate, gapToleranceFrames: 0);
        var packetFrames = sampleRate / 10;

        var first = timeline.PlanPacket(TimeSpan.FromMilliseconds(100).Ticks, packetFrames);
        var afterGap = timeline.PlanPacket(TimeSpan.FromMilliseconds(400).Ticks, packetFrames);

        Assert.Equal(0, first.SilenceFrames);
        Assert.Equal(sampleRate / 5, afterGap.SilenceFrames);
        Assert.Equal(sampleRate * 4L / 10, timeline.PositionFrames);
    }

    [Fact]
    public void PacketAtSilenceCheckBoundary_DoesNotDuplicateFrames()
    {
        const int sampleRate = 48000;
        var timeline = new LoopbackFrameTimeline(sampleRate, gapToleranceFrames: 0);
        var packetFrames = sampleRate / 10;

        timeline.PlanPacket(TimeSpan.FromMilliseconds(100).Ticks, packetFrames);
        var second = timeline.PlanPacket(TimeSpan.FromMilliseconds(200).Ticks, packetFrames);

        Assert.Equal(0, second.SilenceFrames);
        Assert.Equal(sampleRate / 5, timeline.PositionFrames);
    }

    [Fact]
    public void Complete_AddsTrailingSilenceOnlyToElapsedTime()
    {
        const int sampleRate = 48000;
        var timeline = new LoopbackFrameTimeline(sampleRate, gapToleranceFrames: 0);

        timeline.PlanPacket(TimeSpan.FromMilliseconds(100).Ticks, sampleRate / 10);
        var trailingSilence = timeline.PlanCompletion(TimeSpan.FromMilliseconds(350).Ticks);

        Assert.Equal(sampleRate / 4, trailingSilence);
        Assert.Equal(sampleRate * 35L / 100, timeline.PositionFrames);
    }
}
