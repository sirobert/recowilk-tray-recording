using MeetingAudioRecorder.TranslationWorker;
using Xunit;

public class SentenceTests
{
    [Theory]
    [InlineData("The train is late. We need more time.", "The train is late.", "We need more time.")]
    [InlineData("Stop! Do not delete it.", "Stop!", "Do not delete it.")]
    [InlineData("Can you hear me? Yes.", "Can you hear me?", "Yes.")]
    [InlineData("He said, \"Wait.\" Then left.", "He said, \"Wait.\"", "Then left.")]
    [InlineData("The price is 3.14 dollars. Pay today.", "The price is 3.14 dollars.", "Pay today.")]
    [InlineData("Dr. Smith called. Call him back.", "Dr. Smith called.", "Call him back.")]
    [InlineData("J. Smith works in the U.S. office. He called.", "J. Smith works in the U.S. office.", "He called.")]
    [InlineData("Use e.g. a pencil. Write clearly.", "Use e.g. a pencil.", "Write clearly.")]
    [InlineData("Visit example.com today. Email a@b.com tomorrow.", "Visit example.com today.", "Email a@b.com tomorrow.")]
    [InlineData("Wait... Then continue.", "Wait...", "Then continue.")]
    public void FindsBoundariesWithoutSplittingProtectedPunctuation(string input, string first, string second)
        => Assert.Equal(new[] { first, second }, EnglishSentenceSplitter.Split(input).Select(p => p.Text));

    [Theory]
    [InlineData("  First.\r\n\tSecond?  Third without punctuation  ")]
    [InlineData("The meeting starts at nine tomorrow morning. Please send the report today, and let me know if you need more time.")]
    [InlineData("A sentence without punctuation and with a number 3.14")]
    public void EveryNonWhitespaceCharacterIsPreservedExactlyOnce(string input)
    {
        var parts = EnglishSentenceSplitter.Split(input);
        Assert.Equal(string.Concat(input.Where(c => !char.IsWhiteSpace(c))),
            string.Concat(string.Concat(parts.Select(p => p.Text)).Where(c => !char.IsWhiteSpace(c))));
        int previousEnd = 0;
        foreach (var part in parts)
        {
            Assert.True(part.Start >= previousEnd);
            Assert.Equal(input.Substring(part.Start, part.Length), part.Text);
            previousEnd = part.Start + part.Length;
        }
    }

    [Fact]
    public void EmptyInputProducesNoTranslationUnits() => Assert.Empty(EnglishSentenceSplitter.Split(" \r\n\t"));
}
