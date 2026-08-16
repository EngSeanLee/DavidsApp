namespace DavidsApp.Client.Services.Speech;

public enum VoiceCommand
{
    None,
    Pause,
    Resume,
    Cancel,
    Save,
    Repeat,
}

/// <summary>
/// Matches a transcript against the small fixed set of control phrases in docs/state-machine.md
/// §"Voice command routing". Deliberately conservative: only short utterances (≤4 words) get
/// checked, and only against near-exact phrasing — never substring-contains on arbitrary-length
/// dictated text — so a finding that happens to mention "pause" mid-sentence doesn't misfire.
/// Pure logic, no platform dependency, so it's unit-testable and reusable by both the Android and
/// Windows speech recognizer implementations.
/// </summary>
public static class CommandWordDetector
{
    private const int MaxCommandWordCount = 4;

    private static readonly Dictionary<VoiceCommand, string[]> Phrases = new()
    {
        [VoiceCommand.Pause] = ["pause", "hold on"],
        [VoiceCommand.Resume] = ["resume", "go ahead"],
        [VoiceCommand.Cancel] = ["cancel", "scratch that"],
        [VoiceCommand.Save] = ["save", "confirm"],
        [VoiceCommand.Repeat] = ["repeat"],
    };

    public static VoiceCommand Detect(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return VoiceCommand.None;
        }

        var normalized = transcript.Trim().ToLowerInvariant();
        // Strip trailing punctuation a recognizer might emit (e.g. "pause." / "save!").
        normalized = normalized.TrimEnd('.', '!', '?');

        var wordCount = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > MaxCommandWordCount)
        {
            return VoiceCommand.None;
        }

        foreach (var (command, phrases) in Phrases)
        {
            if (phrases.Any(phrase => normalized == phrase))
            {
                return command;
            }
        }

        return VoiceCommand.None;
    }
}
