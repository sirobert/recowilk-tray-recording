using MeetingAudioRecorder.Core.Services;

namespace MeetingAudioRecorder.Core.Tests;

public class FileNameServiceTests
{
    private readonly FileNameService _sut = new();

    [Fact]
    public void GenerateFileName_DefaultFormat_ContainsDateAndExtension()
    {
        var ts = new DateTimeOffset(2026, 7, 24, 10, 30, 0, TimeSpan.Zero);
        var name = _sut.GenerateFileName("Nagranie_yyyy-MM-dd_HH-mm-ss.mp3", ts);
        Assert.EndsWith(".mp3", name);
        Assert.Contains("2026-07-24", name);
        Assert.Contains("Nagranie_", name);
    }

    [Fact]
    public void GenerateFileName_EmptyFormat_UsesFallback()
    {
        var name = _sut.GenerateFileName("", DateTimeOffset.Now);
        Assert.EndsWith(".mp3", name);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void EnsureUniquePath_NoCollision_ReturnsSame()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mar-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = _sut.EnsureUniquePath(dir, "test.mp3");
            Assert.Equal(Path.Combine(dir, "test.mp3"), path);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EnsureUniquePath_Collision_AddsNumber()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mar-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "test.mp3"), "x");
            var path = _sut.EnsureUniquePath(dir, "test.mp3");
            Assert.Equal(Path.Combine(dir, "test_2.mp3"), path);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EnsureUniquePath_MultipleCollisions_Increments()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mar-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "nagranie.mp3"), "a");
            File.WriteAllText(Path.Combine(dir, "nagranie_2.mp3"), "b");
            var path = _sut.EnsureUniquePath(dir, "nagranie.mp3");
            Assert.Equal(Path.Combine(dir, "nagranie_3.mp3"), path);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
