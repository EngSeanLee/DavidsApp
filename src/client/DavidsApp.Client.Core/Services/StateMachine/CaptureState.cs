namespace DavidsApp.Client.Services.StateMachine;

/// <summary>
/// See docs/state-machine.md. Saved/Deleted are deliberately NOT states here — the doc describes
/// them as resetting straight back to Idle, so they're modeled as transient events
/// (CaptureStateMachine.FindingSaved / FindingDeleted) instead of resting states.
/// </summary>
public enum CaptureState
{
    Idle,
    ListeningParsing,
    Confirm,
    MissingField,
    UnknownVocabulary,

    /// <summary>OS suspended the mic mid-capture (screen lock, incoming call, app switch). Not in
    /// the original spec — added during technical review. Resume() restores the prior state.</summary>
    Paused,

    /// <summary>An API call failed (network or server error). Retry() restores the prior state so
    /// in-progress context (pendingRow, which field was missing) isn't lost.</summary>
    SpeechFailed,
}
