using MeetingAudioRecorder.Core.Interfaces;
using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Infrastructure.Recovery;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingAudioRecorder.Audio.Tests;

public class RecordingSessionManifestStoreTests
{
    [Fact]
    public void SaveLoadDelete_RoundTripsVersionedManifestWithoutPartials()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mar-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var id = Guid.NewGuid();
            var startedAt = DateTimeOffset.Parse("2026-07-26T12:34:56+02:00");
            var sut = new JsonRecordingSessionManifestStore(
                NullLogger<JsonRecordingSessionManifestStore>.Instance,
                directory);

            sut.Save(new RecordingSessionManifest
            {
                RecordingId = id,
                StartedAt = startedAt,
                State = "recording",
                MicrophoneTempPath = "mic.wav",
                LoopbackTempPath = "loop.wav",
                MicrophoneFormat = new WaveFormatInfo
                {
                    SampleRate = 48000,
                    Channels = 2,
                    BitsPerSample = 32,
                    Encoding = "IeeeFloat"
                }
            });

            var loaded = sut.TryLoad(id);

            Assert.NotNull(loaded);
            Assert.Equal(1, loaded.Version);
            Assert.Equal(startedAt, loaded.StartedAt);
            Assert.Equal(48000, loaded.MicrophoneFormat!.SampleRate);
            Assert.Empty(Directory.EnumerateFiles(directory, "*.partial.*"));

            sut.Delete(id);
            Assert.Null(sut.TryLoad(id));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
