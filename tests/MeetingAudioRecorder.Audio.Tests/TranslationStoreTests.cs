using MeetingAudioRecorder.Core.Models;
using MeetingAudioRecorder.Infrastructure.Translation;

namespace MeetingAudioRecorder.Audio.Tests;

public sealed class TranslationStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mar-translation-test-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public void FinalSurvivesRestartAndDraftIsNotStored()
    {
        var store = new TranslationStore(root);
        var session = new TranslationSession { Title = "Private meeting", Sources = "output" };
        store.Create(session);
        store.Append(session.Id, new TranslationCaption("output", 1, "Secret English", "Tajny polski", false, 100, 2200, "text"));
        store.Append(session.Id, new TranslationCaption("output", 2, "draft", "roboczy", true, 2200, 3000, "text"));
        var recovered = new TranslationStore(root).Load(session.Id);
        Assert.Single(recovered.Captions);
        Assert.Equal("Secret English", recovered.Captions[0].English);
        Assert.Equal("running", recovered.Session.State);
        Assert.DoesNotContain("Secret English", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(Path.Combine(root, session.Id.ToString("N")), "*.segment").Single())));
    }
    [Fact]
    public void DuplicateFinalIsIdempotentAndSourcesHaveIndependentIds()
    {
        var store = new TranslationStore(root); var session = new TranslationSession(); store.Create(session);
        var c = new TranslationCaption("output", 1, "One", "Jeden", false, 0, 1000, "text");
        store.Append(session.Id, c); store.Append(session.Id, c); store.Append(session.Id, c with { Source = "microphone" });
        store.Complete(session.Id, "completed");
        Assert.Equal(2, store.Load(session.Id).Captions.Count);
        Assert.Equal("completed", store.List().Single().State);
        Assert.Empty(Directory.GetFiles(root, "*.mp3", SearchOption.AllDirectories));
    }
    [Fact]
    public void DamagedSegmentDoesNotHideOtherSavedSegments()
    {
        var store = new TranslationStore(root); var session = new TranslationSession(); store.Create(session);
        store.Append(session.Id, new TranslationCaption("output", 1, "one", "jeden", false, 0, 1000, "text"));
        File.WriteAllText(Path.Combine(root, session.Id.ToString("N"), "broken.segment"), "broken");
        var result = store.Load(session.Id); Assert.Single(result.Captions); Assert.Equal(1, result.UnreadableSegments);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
