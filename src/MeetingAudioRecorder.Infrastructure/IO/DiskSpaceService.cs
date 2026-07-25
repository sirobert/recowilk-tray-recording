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
}
