using Android.Content;
using Android.OS;
using Android.Speech;
using DavidsApp.Client.Services.Speech;
using Microsoft.Extensions.Logging;
using Application = Android.App.Application;

namespace DavidsApp.Client.Platforms.Android.Speech;

/// <summary>
/// Android has no public "always listening" recognizer API — android.speech.SpeechRecognizer
/// times out after a few seconds of silence. This wraps it with the standard community workaround:
/// tear down and immediately start a fresh recognizer session on every result/timeout/no-match, so
/// the net effect is continuous-enough capture from the caller's point of view. See
/// docs/decisions/0001-continuous-stt-approach.md.
///
/// IMPORTANT — not verified on a device or emulator in this environment (no Android runtime
/// available here). Written carefully against the documented SpeechRecognizer/RecognitionListener
/// API, but flagged per the ADR's risk list as needing real on-device validation before relying on
/// it: expect small gaps between sessions, possible start/stop chimes, and OS version differences
/// in RECORD_AUDIO permission/behavior.
/// </summary>
public sealed class AndroidContinuousSpeechRecognizer : Java.Lang.Object, IContinuousSpeechRecognizer, IRecognitionListener
{
    private readonly ILogger<AndroidContinuousSpeechRecognizer> _logger;

    private global::Android.Speech.SpeechRecognizer? _recognizer;
    private bool _isMuted;
    private bool _stoppedIntentionally;

    public AndroidContinuousSpeechRecognizer(ILogger<AndroidContinuousSpeechRecognizer> logger)
    {
        _logger = logger;
    }

    public bool IsListening { get; private set; }

    public event EventHandler<string>? PartialResult;
    public event EventHandler<string>? FinalResult;
    public event EventHandler<string>? Error;

    public Task StartListeningAsync(CancellationToken ct = default)
    {
        _stoppedIntentionally = false;
        _isMuted = false;
        MainThread.BeginInvokeOnMainThread(BeginSession);
        IsListening = true;
        return Task.CompletedTask;
    }

    public Task StopListeningAsync()
    {
        _stoppedIntentionally = true;
        MainThread.BeginInvokeOnMainThread(TeardownCurrentSession);
        IsListening = false;
        return Task.CompletedTask;
    }

    public void Mute()
    {
        if (_isMuted) return;
        _isMuted = true;
        // Actually stop capturing (not just discard results) — TTS coordination requires the mic
        // isn't live while the app speaks, per spec §5.3, to avoid the recognizer picking up the
        // app's own voice output.
        MainThread.BeginInvokeOnMainThread(TeardownCurrentSession);
    }

    public void Unmute()
    {
        if (!_isMuted) return;
        _isMuted = false;
        if (IsListening)
        {
            MainThread.BeginInvokeOnMainThread(BeginSession);
        }
    }

    private void BeginSession()
    {
        TeardownCurrentSession();

        if (!global::Android.Speech.SpeechRecognizer.IsRecognitionAvailable(Application.Context))
        {
            _logger.LogWarning("SpeechRecognizer.IsRecognitionAvailable returned false on this device.");
            Error?.Invoke(this, "Speech recognition is not available on this device.");
            return;
        }

        _recognizer = global::Android.Speech.SpeechRecognizer.CreateSpeechRecognizer(Application.Context);
        if (_recognizer is null)
        {
            _logger.LogWarning("SpeechRecognizer.CreateSpeechRecognizer returned null.");
            Error?.Invoke(this, "Couldn't create the speech recognizer.");
            return;
        }
        _recognizer.SetRecognitionListener(this);

        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
        intent.PutExtra(RecognizerIntent.ExtraCallingPackage, Application.Context.PackageName);
        // Tuned longer than the ~1s Android default, to reduce premature cutoffs mid-finding
        // (spec §5.1's "avoid false-positive... in loud environments" concern applies here too —
        // cutting a dictated finding short is its own reliability problem). Needs field tuning.
        intent.PutExtra(RecognizerIntent.ExtraSpeechInputCompleteSilenceLengthMillis, 2000L);
        intent.PutExtra(RecognizerIntent.ExtraSpeechInputPossiblyCompleteSilenceLengthMillis, 2000L);

        _recognizer.StartListening(intent);
    }

    private void TeardownCurrentSession()
    {
        if (_recognizer is null) return;
        try
        {
            _recognizer.StopListening();
            _recognizer.Cancel();
            _recognizer.Destroy();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error tearing down SpeechRecognizer session (usually harmless).");
        }
        finally
        {
            _recognizer.SetRecognitionListener(null);
            _recognizer = null;
        }
    }

    private void RestartIfStillListening()
    {
        if (_stoppedIntentionally || _isMuted || !IsListening) return;
        MainThread.BeginInvokeOnMainThread(BeginSession);
    }

    // --- IRecognitionListener ---

    public void OnResults(Bundle? results)
    {
        var text = ExtractBestResult(results);
        if (!string.IsNullOrWhiteSpace(text))
        {
            FinalResult?.Invoke(this, text);
        }
        RestartIfStillListening();
    }

    public void OnPartialResults(Bundle? partialResults)
    {
        var text = ExtractBestResult(partialResults);
        if (!string.IsNullOrWhiteSpace(text))
        {
            PartialResult?.Invoke(this, text);
        }
    }

    public void OnError(SpeechRecognizerError error)
    {
        // ERROR_NO_MATCH and ERROR_SPEECH_TIMEOUT are routine session churn, not real errors —
        // the restart loop handles them silently. Anything else surfaces to the UI.
        if (error is not (SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout))
        {
            _logger.LogWarning("SpeechRecognizer error: {Error}", error);
            Error?.Invoke(this, error.ToString());
        }
        RestartIfStillListening();
    }

    private static string? ExtractBestResult(Bundle? bundle)
    {
        var matches = bundle?.GetStringArrayList(global::Android.Speech.SpeechRecognizer.ResultsRecognition);
        return matches is { Count: > 0 } ? matches[0] : null;
    }

    public void OnReadyForSpeech(Bundle? @params) { }
    public void OnBeginningOfSpeech() { }
    public void OnRmsChanged(float rmsdB) { }
    public void OnBufferReceived(byte[]? buffer) { }
    public void OnEndOfSpeech() { }
    public void OnEvent(int eventType, Bundle? @params) { }
}
