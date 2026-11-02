using MeetingAudioRecorder.TranslationWorker;
using Xunit;

public class SpeechTranslationInputTests
{
    [Theory]
    [InlineData("[INAUDIBLE]")]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData(" [ blank_audio ] ")]
    [InlineData("(audience member speaking off microphone)")]
    [InlineData(" [ Inaudible ] ")]
    [InlineData("[unintelligible].")]
    public void MarkerOnlyNeverBecomesModelInput(string input)
    {
        var parts = SpeechTranslationInput.Split(input);
        Assert.All(parts, p => Assert.True(p.IsLiteral));
        Assert.Equal("unrecognized", SpeechTranslationInput.Status(parts));
        Assert.Contains(parts, p => SpeechTranslationInput.Literal(p) == "[nie rozpoznano mowy]");
    }

    [Theory]
    [InlineData("[INAUDIBLE]")]
    [InlineData("[BLANK_AUDIO]")]
    public void MixedSpeechPreservesBothSidesAndExactSourceOffsets(string marker)
    {
        string text = $"Do not delete it. {marker} Keep three files.";
        var parts = SpeechTranslationInput.Split(text);
        Assert.Equal("partial-recognition", SpeechTranslationInput.Status(parts));
        Assert.Equal(new[] { "Do not delete it.", "Keep three files." }, parts.Where(p => !p.IsLiteral).Select(p => p.Text));
        Assert.All(parts, p => Assert.Equal(p.Text, text.Substring(p.Start, p.Length)));
    }

    [Theory]
    [InlineData("The label says [approved].")]
    [InlineData("The word inaudible is written here.")]
    [InlineData("[INAUDIBLE speech]")]
    public void OrdinaryTextIsNotMistakenForMarker(string text)
    {
        Assert.DoesNotContain(SpeechTranslationInput.Split(text), p => p.IsUnrecognized);
    }

    [Fact]
    public void EmptyRecognitionIsExplicit()
    {
        Assert.Equal("no-speech-text", SpeechTranslationInput.Status(SpeechTranslationInput.Split("   ")));
    }
}
