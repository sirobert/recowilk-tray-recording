namespace MeetingAudioRecorder.Core.Interfaces;

public interface IFileNameService
{
    string GenerateFileName(string format, DateTimeOffset timestamp);
    string EnsureUniquePath(string directory, string fileName);
    bool IsValidFileNameFormat(string format, out string? error);
}

public interface IDiskSpaceService
{
    bool HasEnoughSpace(string directory, long requiredBytes, out long availableBytes);
    long EstimateRequiredBytes(TimeSpan estimatedDuration, int bitrateKbps);
}

public interface ISingleInstanceService : IDisposable
{
    bool TryAcquire();
    void SignalFirstInstance();
    event EventHandler? SecondInstanceDetected;
}
