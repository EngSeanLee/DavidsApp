using DavidsApp.Client.Services.Speech;
using Xunit;

namespace DavidsApp.Client.Core.Tests;

public class CommandWordDetectorTests
{
    [Theory]
    [InlineData("pause", VoiceCommand.Pause)]
    [InlineData("Pause", VoiceCommand.Pause)]
    [InlineData("hold on", VoiceCommand.Pause)]
    [InlineData("resume", VoiceCommand.Resume)]
    [InlineData("go ahead", VoiceCommand.Resume)]
    [InlineData("cancel", VoiceCommand.Cancel)]
    [InlineData("scratch that", VoiceCommand.Cancel)]
    [InlineData("save", VoiceCommand.Save)]
    [InlineData("confirm", VoiceCommand.Save)]
    [InlineData("repeat", VoiceCommand.Repeat)]
    [InlineData("save!", VoiceCommand.Save)]
    [InlineData("  pause  ", VoiceCommand.Pause)]
    public void Recognizes_exact_control_phrases(string transcript, VoiceCommand expected)
    {
        Assert.Equal(expected, CommandWordDetector.Detect(transcript));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_or_whitespace_transcript_is_None(string? transcript)
    {
        Assert.Equal(VoiceCommand.None, CommandWordDetector.Detect(transcript));
    }

    [Theory]
    [InlineData("kitchen north trim white wood intact sill 1.2")]
    [InlineData("please pause the reading at the north wall")] // "pause" appears but sentence is long
    [InlineData("cancel the old paint and use white instead")] // "cancel" appears mid-sentence, not the whole utterance
    public void Long_dictated_findings_are_not_misdetected_as_commands(string transcript)
    {
        Assert.Equal(VoiceCommand.None, CommandWordDetector.Detect(transcript));
    }

    [Fact]
    public void Utterance_over_four_words_never_matches_even_if_short_phrase_present()
    {
        Assert.Equal(VoiceCommand.None, CommandWordDetector.Detect("please just go ahead now"));
    }
}
