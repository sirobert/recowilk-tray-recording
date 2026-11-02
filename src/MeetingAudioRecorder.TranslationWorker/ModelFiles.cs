using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
namespace MeetingAudioRecorder.TranslationWorker;

internal static class ModelFiles
{
    internal static SessionOptions Options(int threads) => new() { IntraOpNumThreads = threads, InterOpNumThreads = 1, GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
    internal static void Verify(string path, string hash, long bytes)
    {
        using var file = File.OpenRead(path);
        if (file.Length != bytes || !Convert.ToHexString(SHA256.HashData(file)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Nieprawidłowy model: " + Path.GetFileName(path));
    }
}
