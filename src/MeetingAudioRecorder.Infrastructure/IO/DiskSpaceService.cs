using MeetingAudioRecorder.Core.Interfaces;

namespace MeetingAudioRecorder.Infrastructure.IO;

public sealed class DiskSpaceService : IDiskSpaceService
{
    public bool HasEnoughSpace(string directory, long requiredBytes, out long availableBytes)
    {
        availableBytes = 0;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrEmpty(root))
                return true;

            var drive = new DriveInfo(root);
            availableBytes = drive.AvailableFreeSpace;
            return availableBytes >= requiredBytes;
        }
        catch
        {
            // W razie błędu nie blokuj
            availableBytes = long.MaxValue;
            return true;
        }
    }

    public long EstimateRequiredBytes(TimeSpan estimatedDuration, int bitrateKbps)
    {
        // MP3 + zapas na WAV tymczasowe (nieskompresowane ~ stereo 48kHz 32bit ≈ 384 KB/s * 2 źródła)
        var mp3Bytes = (long)(estimatedDuration.TotalSeconds * bitrateKbps * 1000 / 8);
        var wavBytes = (long)(estimatedDuration.TotalSeconds * 48000 * 2 * 4 * 2); // 2 tracks float stereo
        return mp3Bytes + wavBytes + (50 * 1024 * 1024);
    }
}
