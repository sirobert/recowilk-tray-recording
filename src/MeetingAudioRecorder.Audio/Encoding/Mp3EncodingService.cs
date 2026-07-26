using MeetingAudioRecorder.Core.Interfaces;
using Microsoft.Extensions.Logging;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace MeetingAudioRecorder.Audio.Encoding;

/// <summary>
/// Kodowanie WAV → MP3 przez Windows Media Foundation (bez FFmpeg).
/// </summary>
public sealed class Mp3EncodingService : IMp3EncodingService
{
    private readonly ILogger<Mp3EncodingService> _logger;
    private static int _mfStarted;

    public Mp3EncodingService(ILogger<Mp3EncodingService> logger)
    {
        _logger = logger;
        EnsureMediaFoundation();
    }

    public Task EncodeToMp3Async(string inputWavPath, string outputMp3Path, int bitrateKbps, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var encoderOutputPath = GetEncoderOutputPath(outputMp3Path);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(inputWavPath))
                    throw new FileNotFoundException("Brak pliku WAV do zakodowania.", inputWavPath);

                Directory.CreateDirectory(Path.GetDirectoryName(outputMp3Path)!);

                // Usuń ewentualny stary partial
                if (File.Exists(outputMp3Path))
                    File.Delete(outputMp3Path);
                if (!PathsEqual(encoderOutputPath, outputMp3Path) && File.Exists(encoderOutputPath))
                    File.Delete(encoderOutputPath);

                _logger.LogInformation("Kodowanie MP3 {Bitrate}kbps: {In} → {Out}", bitrateKbps, inputWavPath, outputMp3Path);

                using var reader = new AudioFileReader(inputWavPath);

                // Media Foundation wymaga PCM 16-bit do wielu encoderów.
                // Token jest sprawdzany przy każdym odczycie źródła; finalizacja MF może pozostać synchroniczna.
                var pcm16 = new CancellationWaveProvider(reader.ToWaveProvider16(), cancellationToken);

                try
                {
                    MediaFoundationEncoder.EncodeToMp3(pcm16, encoderOutputPath, bitrateKbps * 1000);
                }
                catch (InvalidOperationException)
                {
                    // Fallback: niektóre systemy wymagają jawnego media type
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogWarning("EncodeToMp3 nie powiodło się, próba ręcznego enkodera MF");
                    reader.Position = 0;
                    var fallback = new CancellationWaveProvider(reader.ToWaveProvider16(), cancellationToken);
                    EncodeWithManualMediaType(fallback, encoderOutputPath, bitrateKbps);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(encoderOutputPath) || new FileInfo(encoderOutputPath).Length < 128)
                    throw new InvalidOperationException("Media Foundation nie utworzyła poprawnego pliku MP3. Sprawdź, czy system obsługuje koder MP3.");

                if (!PathsEqual(encoderOutputPath, outputMp3Path))
                    File.Move(encoderOutputPath, outputMp3Path);

                _logger.LogInformation("MP3 gotowy: {Path}, rozmiar={Size}", outputMp3Path, new FileInfo(outputMp3Path).Length);
            }
            catch
            {
                TryDelete(encoderOutputPath);
                TryDelete(outputMp3Path);
                throw;
            }
        }, cancellationToken);
    }

    private static string GetEncoderOutputPath(string outputPath) =>
        string.Equals(Path.GetExtension(outputPath), ".mp3", StringComparison.OrdinalIgnoreCase)
            ? outputPath
            : outputPath + ".encoding.mp3";

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            StringComparison.OrdinalIgnoreCase);

    private static void EncodeWithManualMediaType(IWaveProvider source, string outputPath, int bitrateKbps)
    {
        var mediaType = MediaFoundationEncoder.SelectMediaType(
            AudioSubtypes.MFAudioFormat_MP3,
            source.WaveFormat,
            bitrateKbps * 1000);

        if (mediaType is null)
            throw new InvalidOperationException(
                "System Windows nie udostępnia kodera MP3 Media Foundation. " +
                "Zainstaluj opcjonalne funkcje multimedialne lub użyj Windows 10/11 z obsługą MF.");

        using var encoder = new MediaFoundationEncoder(mediaType);
        encoder.Encode(outputPath, source);
    }

    private static void EnsureMediaFoundation()
    {
        if (Interlocked.Exchange(ref _mfStarted, 1) == 0)
        {
            try
            {
                MediaFoundationApi.Startup();
            }
            catch
            {
                Interlocked.Exchange(ref _mfStarted, 0);
                throw;
            }
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
            // Koordynator ponowi cleanup; plik nadal ma rozszerzenie .partial.
        }
    }
}
