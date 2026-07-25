using MeetingAudioRecorder.Core.Services;

namespace MeetingAudioRecorder.Core.Tests;

public class AudioMathTests
{
    [Fact]
    public void CalculateLeadingSilence_LaterSource_ReturnsSamples()
    {
        // 100ms delay at 48000 Hz stereo = 4800 frames * 2 channels = 9600 samples
        var delayTicks = TimeSpan.FromMilliseconds(100).Ticks;
        var samples = AudioMath.CalculateLeadingSilenceSamples(delayTicks, 0, 48000, 2);
        Assert.Equal(9600, samples);
    }

    [Fact]
    public void CalculateLeadingSilence_EarlierSource_ReturnsZero()
    {
        var samples = AudioMath.CalculateLeadingSilenceSamples(0, TimeSpan.FromMilliseconds(50).Ticks, 48000, 2);
        Assert.Equal(0, samples);
    }

    [Fact]
    public void MixSamples_NoClippingBelowThreshold()
    {
        var m = AudioMath.MixSamples(0.3f, 0.3f, 1f, 0.85f);
        Assert.InRange(m, 0.5f, 0.6f);
    }

    [Fact]
    public void SoftLimit_DoesNotExceedOne()
    {
        var limited = AudioMath.SoftLimit(5f);
        Assert.True(limited < 1f);
        Assert.True(limited > 0.9f);
    }

    [Fact]
    public void SoftLimit_PreservesSmallSamples()
    {
        Assert.Equal(0.5f, AudioMath.SoftLimit(0.5f));
        Assert.Equal(-0.2f, AudioMath.SoftLimit(-0.2f));
    }

    [Fact]
    public void HardClip_Clamps()
    {
        Assert.Equal(1f, AudioMath.HardClip(2f));
        Assert.Equal(-1f, AudioMath.HardClip(-3f));
    }

    [Fact]
    public void MixBuffers_AppliesVolumesAndLimits()
    {
        var mic = new float[] { 0.5f, 0.5f };
        var loop = new float[] { 0.5f, 0.5f };
        var output = new float[2];
        AudioMath.MixBuffers(mic, loop, output, 1f, 1f);
        Assert.All(output, s => Assert.True(s < 1.0f));
    }

    [Fact]
    public void CreateSilence_AllZeros()
    {
        var s = AudioMath.CreateSilence(100);
        Assert.Equal(100, s.Length);
        Assert.All(s, x => Assert.Equal(0f, x));
    }

    [Fact]
    public void CalculatePeak_FindsMax()
    {
        Assert.Equal(0.8f, AudioMath.CalculatePeak(new[] { 0.1f, -0.8f, 0.3f }));
    }
}
