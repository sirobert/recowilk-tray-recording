using System.Text;
using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;

namespace MeetingAudioRecorder.Infrastructure.Recovery;

/// <summary>
/// Rebuilds RIFF and data sizes in a separate, atomically published WAV copy.
/// The source file is never modified.
/// </summary>
public sealed class WavFileRepairService : IWavFileRepairService
{
    public bool CanRecover(string sourcePath)
    {
        try
        {
            _ = Inspect(sourcePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public WavRepairResult RepairToCopy(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var sourceFullPath = Path.GetFullPath(sourcePath);
        var destinationFullPath = Path.GetFullPath(destinationPath);
        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Plik naprawiony musi mieć inną ścieżkę niż źródło.");

        var info = Inspect(sourceFullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFullPath)!);
        var partialPath = destinationFullPath + ".partial." + Guid.NewGuid().ToString("N");

        try
        {
            using (var source = new FileStream(sourceFullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var destination = new FileStream(partialPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                CopyBytes(source, destination, info.DataOffset + info.DataLengthBytes);

                using var writer = new BinaryWriter(destination, Encoding.ASCII, leaveOpen: true);
                destination.Position = 4;
                writer.Write(checked((uint)(destination.Length - 8)));
                destination.Position = info.DataSizeOffset;
                writer.Write(checked((uint)info.DataLengthBytes));
                writer.Flush();
                destination.Flush(flushToDisk: true);
            }

            var repaired = Inspect(partialPath);
            if (repaired.DataLengthBytes != info.DataLengthBytes || repaired.BlockAlign != info.BlockAlign)
                throw new InvalidDataException("Walidacja naprawionej kopii WAV nie powiodła się.");

            File.Move(partialPath, destinationFullPath, overwrite: true);
            return new WavRepairResult
            {
                Success = true,
                OutputPath = destinationFullPath,
                DataLengthBytes = repaired.DataLengthBytes,
                BlockAlign = repaired.BlockAlign
            };
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    private static WavLayout Inspect(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        if (stream.Length < 12 || ReadFourCc(reader) != "RIFF")
            throw new InvalidDataException("Brak nagłówka RIFF.");

        _ = reader.ReadUInt32();
        if (ReadFourCc(reader) != "WAVE")
            throw new InvalidDataException("Brak sygnatury WAVE.");

        int? blockAlign = null;
        long position = 12;
        while (position + 8 <= stream.Length)
        {
            stream.Position = position;
            var chunkId = ReadFourCc(reader);
            var declaredSize = reader.ReadUInt32();
            var chunkDataOffset = position + 8;

            if (chunkId == "fmt ")
            {
                if (declaredSize < 16 || chunkDataOffset + 16 > stream.Length)
                    throw new InvalidDataException("Niepełny chunk fmt.");

                stream.Position = chunkDataOffset + 12;
                blockAlign = reader.ReadUInt16();
                if (blockAlign <= 0)
                    throw new InvalidDataException("Nieprawidłowy BlockAlign.");
            }
            else if (chunkId == "data")
            {
                if (blockAlign is null)
                    throw new InvalidDataException("Chunk data występuje przed fmt.");

                var available = stream.Length - chunkDataOffset;
                var candidateLength = declaredSize == 0 || declaredSize > available
                    ? available
                    : declaredSize;
                var alignedLength = candidateLength - candidateLength % blockAlign.Value;
                if (alignedLength <= 0)
                    throw new InvalidDataException("WAV nie zawiera pełnej ramki audio.");

                return new WavLayout(
                    DataOffset: chunkDataOffset,
                    DataSizeOffset: position + 4,
                    DataLengthBytes: alignedLength,
                    BlockAlign: blockAlign.Value);
            }

            var next = chunkDataOffset + declaredSize + (declaredSize & 1);
            if (next <= position || next > stream.Length)
                throw new InvalidDataException("Nieprawidłowy rozmiar chunku WAV.");
            position = next;
        }

        throw new InvalidDataException("Brak chunku data.");
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length != 4)
            throw new EndOfStreamException();
        return Encoding.ASCII.GetString(bytes);
    }

    private static void CopyBytes(Stream source, Stream destination, long count)
    {
        var buffer = new byte[64 * 1024];
        while (count > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0)
                throw new EndOfStreamException();
            destination.Write(buffer, 0, read);
            count -= read;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A stale uniquely named partial is harmless and never used for recovery.
        }
    }

    private sealed record WavLayout(
        long DataOffset,
        long DataSizeOffset,
        long DataLengthBytes,
        int BlockAlign);
}
