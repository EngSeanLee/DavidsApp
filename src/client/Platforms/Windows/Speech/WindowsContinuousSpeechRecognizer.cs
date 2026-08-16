using DavidsApp.Client.Services.Speech;
using Microsoft.Extensions.Logging;
using Windows.Media.SpeechRecognition;

namespace DavidsApp.Client.Platforms.Windows.Speech;

/// <summary>
/// Windows has a genuinely continuous recognition API (unlike Android — see
/// docs/decisions/0001-continuous-stt-approach.md), so this is a much thinner wrapper:
/// ContinuousRecognitionSession.PauseAsync/ResumeAsync map directly onto Mute/Unmute, no
/// teardown-and-restart loop needed. This is the dev/test target per the spec, not the primary
/// field device, so less field-hardening effort has gone in here than the Android implementation.
///
/// NOT verified running in this environment (no interactive Windows desktop session available
/// here to actually launch the app and speak into a mic) — written against the documented
/// Windows.Media.SpeechRecognition API, needs a real run to confirm.
/// </summary>
public sealed class WindowsContinuousSpeechRecognizer : IContinuousSpeechRecognizer
{
    private readonly ILogger<WindowsContinuousSpeechRecognizer> _logger;
    private global::Windows.Media.SpeechRecognition.SpeechRecognizer? _recognizer;
    private bool _isMuted;

    public WindowsContinuousSpeechRecognizer(ILogger<WindowsContinuousSpeechRecognizer> logger)
    {
        _logger = logger;
    }

    public bool IsListening { get; private set; }

    public event EventHandler<string>? PartialResult;
    public event EventHandler<string>? FinalResult;
    public event EventHandler<string>? Error;

    public async Task StartListeningAsync(CancellationToken ct = default)
    {
        if (IsListening) return;

        _recognizer = new global::Windows.Media.SpeechRecognition.SpeechRecognizer();
        _recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "field-finding-dictation"));

        var compileResult = await _recognizer.CompileConstraintsAsync();
        if (compileResult.Status != SpeechRecognitionResultStatus.Success)
        {
            _logger.LogWarning("SpeechRecognizer constraint compilation failed: {Status}", compileResult.Status);
            Error?.Invoke(this, $"Speech recognition setup failed: {compileResult.Status}");
            return;
        }

        _recognizer.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;
        _recognizer.ContinuousRecognitionSession.Completed += OnSessionCompleted;
        _recognizer.HypothesisGenerated += OnHypothesisGenerated;

        await _recognizer.ContinuousRecognitionSession.StartAsync();
        IsListening = true;
        _isMuted = false;
    }

    public async Task StopListeningAsync()
    {
        if (_recognizer is null) return;
        var wasListening = IsListening;
        // Set before awaiting StopAsync, not after — session hygiene per spec §5.3 ("reset
        // between sessions"): OnSessionCompleted's restart-loop guard checks IsListening, so this
        // must already read false for the duration of the stop, not just once it's finished.
        IsListening = false;
        try
        {
            if (wasListening)
            {
                await _recognizer.ContinuousRecognitionSession.StopAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping ContinuousRecognitionSession (usually harmless).");
        }
        finally
        {
            _recognizer.ContinuousRecognitionSession.ResultGenerated -= OnResultGenerated;
            _recognizer.ContinuousRecognitionSession.Completed -= OnSessionCompleted;
            _recognizer.HypothesisGenerated -= OnHypothesisGenerated;
            _recognizer.Dispose();
            _recognizer = null;
        }
    }

    public async void Mute()
    {
        if (_isMuted || _recognizer is null || !IsListening) return;
        _isMuted = true;
        try
        {
            await _recognizer.ContinuousRecognitionSession.PauseAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error pausing ContinuousRecognitionSession.");
        }
    }

    public void Unmute()
    {
        if (!_isMuted || _recognizer is null || !IsListening) return;
        _isMuted = false;
        try
        {
            // Unlike PauseAsync, Resume() is synchronous in the WinRT API.
            _recognizer.ContinuousRecognitionSession.Resume();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resuming ContinuousRecognitionSession.");
        }
    }

    private void OnResultGenerated(SpeechContinuousRecognitionSession session, SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        if (args.Result.Confidence is SpeechRecognitionConfidence.Medium or SpeechRecognitionConfidence.High)
        {
            FinalResult?.Invoke(this, args.Result.Text);
        }
    }

    private void OnHypothesisGenerated(global::Windows.Media.SpeechRecognition.SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
    {
        PartialResult?.Invoke(this, args.Hypothesis.Text);
    }

    private async void OnSessionCompleted(SpeechContinuousRecognitionSession session, SpeechContinuousRecognitionCompletedEventArgs args)
    {
        // Session can end on its own (e.g. SpeechRecognitionResultStatus.TimeoutExceeded after
        // extended silence) — restart it so "continuous" actually holds, same principle as
        // Android's restart loop just triggered far less often here.
        if (args.Status != SpeechRecognitionResultStatus.Success && IsListening && !_isMuted)
        {
            _logger.LogInformation("ContinuousRecognitionSession completed unexpectedly ({Status}), restarting.", args.Status);
            try
            {
                await session.StartAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restart ContinuousRecognitionSession.");
                Error?.Invoke(this, "Speech recognition stopped unexpectedly.");
                IsListening = false;
            }
        }
    }
}
