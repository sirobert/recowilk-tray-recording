using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Core.Interfaces;

public interface IWavFileRepairService
{
    bool CanRecover(string sourcePath);
    WavRepairResult RepairToCopy(string sourcePath, string destinationPath);
}

