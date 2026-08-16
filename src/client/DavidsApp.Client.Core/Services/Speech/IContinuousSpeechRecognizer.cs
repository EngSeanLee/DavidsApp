namespace DavidsApp.Client.Services.Speech;

/// <summary>
/// Platform-agnostic contract for always-on-while-foregrounded speech capture. See
/// docs/decisions/0001-continuous-stt-approach.md — Android and Windows implementations differ
/// substantially under the hood (Android has to restart sessions on silence timeout; Windows has
/// genuine continuous recognition), but both sit behind this interface so the state machine and
/// command-word detection never touch platform APIs directly.
/// </summary>
public interface IContinuousSpeechRecognizer
{
    bool IsListening { get; }

    /// <summary>Starts (or resumes) continuous listening. Safe to call again after Stop().</summary>
    Task StartListeningAsync(CancellationToken ct = default);

    /// <summary>Stops listening and tears down any underlying recognizer session/state.</summary>
    Task StopListeningAsync();

    /// <summary>
    /// Muting keeps the session alive but stops surfacing results — used for "pause"/"hold on"
    /// and for the TTS/STT coordination rule (mic must never listen while the app is speaking).
    /// </summary>
    void Mute();
    void Unmute();

    /// <summary>Fired for interim (not-yet-final) recognition results, if the platform supports them. May not fire on every platform.</summary>
    event EventHandler<string>? PartialResult;

    /// <summary>Fired once a recognition session settles on a final transcript.</summary>
    event EventHandler<string>? FinalResult;

    /// <summary>Fired on a recognizer error. The recognizer is responsible for its own recovery
    /// (e.g. Android's restart-on-silence-timeout loop) — this event is for surfacing
    /// unrecoverable errors (e.g. permission denied) to the UI, not routine session churn.</summary>
    event EventHandler<string>? Error;
}
