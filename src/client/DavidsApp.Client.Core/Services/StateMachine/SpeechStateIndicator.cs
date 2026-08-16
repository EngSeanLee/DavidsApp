namespace DavidsApp.Client.Services.StateMachine;

/// <summary>The UI/TTS status indicator set from docs/state-machine.md.</summary>
public enum SpeechStateIndicator
{
    Ready,
    Listening,
    Processing,
    NeedsResponse,
    ReadyToSave,
    SpeechFailed,
    Paused,
}

public static class SpeechStateIndicatorMapper
{
    /// <summary>Maps CaptureState to the display indicator. ListeningParsing is ambiguous
    /// (covers both "actively listening" and "waiting on the API") — callers pass
    /// isAwaitingApiResponse to disambiguate, since only the ViewModel knows which.</summary>
    public static SpeechStateIndicator From(CaptureState state, bool isAwaitingApiResponse = false) => state switch
    {
        CaptureState.Idle => SpeechStateIndicator.Ready,
        CaptureState.ListeningParsing => isAwaitingApiResponse ? SpeechStateIndicator.Processing : SpeechStateIndicator.Listening,
        CaptureState.Confirm => SpeechStateIndicator.ReadyToSave,
        CaptureState.MissingField => SpeechStateIndicator.NeedsResponse,
        CaptureState.UnknownVocabulary => SpeechStateIndicator.NeedsResponse,
        CaptureState.Paused => SpeechStateIndicator.Paused,
        CaptureState.SpeechFailed => SpeechStateIndicator.SpeechFailed,
        _ => SpeechStateIndicator.Ready,
    };
}
