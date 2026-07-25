using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Services;

public static class StorageBudgetCalculator
{
    public const long CaptureSafetyBytes = 64L * 1024 * 1024;
    public const long ProcessingTempSafetyBytes = 32L * 1024 * 1024;
    public const long OutputSafetyBytes = 16L * 1024 * 1024;

    public static long CalculateCaptureReserve(
        WaveFormatInfo? microphoneFormat,
        WaveFormatInfo? loopbackFormat,
        TimeSpan reserveWindow)
    {
        if (reserveWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(reserveWindow));

        var bytesPerSecond = BytesPerSecond(microphoneFormat) + BytesPerSecond(loopbackFormat);
        return checked(ToBytes(bytesPerSecond, reserveWindow) + CaptureSafetyBytes);
    }

    public static StorageBudget CalculateProcessingBudget(
        TimeSpan duration,
        long sourceBytes,
        int targetSampleRate,
        int bitrateKbps,
        bool keepSeparateTracks)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (sourceBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sourceBytes));
        if (targetSampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSampleRate));
        if (bitrateKbps <= 0)
            throw new ArgumentOutOfRangeException(nameof(bitrateKbps));

        var stereoPcm16BytesPerSecond = checked((long)targetSampleRate * 2 * 2);
        var mixedWavBytes = ToBytes(stereoPcm16BytesPerSecond, duration);
        var conservativeRecoveryBytes = checked(sourceBytes * 3);
        var tempBytes = checked(
            Math.Max(mixedWavBytes, conservativeRecoveryBytes) + ProcessingTempSafetyBytes);

        var mp3Bytes = ToBytes(bitrateKbps * 1000L / 8, duration);
        var separateTracksBytes = keepSeparateTracks
            ? checked(2 * ToBytes(stereoPcm16BytesPerSecond, duration))
            : 0;
        var outputBytes = checked(mp3Bytes + separateTracksBytes + OutputSafetyBytes);

        return new StorageBudget(tempBytes, outputBytes);
    }

    private static long BytesPerSecond(WaveFormatInfo? format)
    {
        if (format is null)
            return 48000L * 2 * 4;

        var bytesPerSample = Math.Max(1, (format.BitsPerSample + 7) / 8);
        return checked((long)format.SampleRate * format.Channels * bytesPerSample);
    }

    private static long ToBytes(long bytesPerSecond, TimeSpan duration)
        => checked((long)Math.Ceiling(bytesPerSecond * duration.TotalSeconds));
}
