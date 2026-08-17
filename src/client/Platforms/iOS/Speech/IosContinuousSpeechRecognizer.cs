using AVFoundation;
using DavidsApp.Client.Services.Speech;
using Foundation;
using Microsoft.Extensions.Logging;
using Speech;

namespace DavidsApp.Client.Platforms.iOS.Speech;

/// <summary>
/// iOS's Speech framework (SFSpeechRecognizer + AVAudioEngine) doesn't impose Android's ~5s
/// silence-timeout wall — a recognition task keeps running until it naturally produces a `Final`
/// result or errors — but a task still ends after each final result, so "continuous" capture is
/// still a restart-on-completion loop, same principle as Android, just triggered less often. See
/// docs/decisions/0001-continuous-stt-approach.md — this implementation was added after the
/// original spec/build (which assumed an Android tablet); it has NOT been verified on real
/// hardware or by a real compile, since producing an iOS build requires a Mac (this was written
/// and pushed for a GitHub Actions macOS runner to build — see .github/workflows/build-ios.yml).
///
/// Requires Info.plist's NSMicrophoneUsageDescription and NSSpeechRecognitionUsageDescription —
/// Apple crashes the process on the permission prompt if either string is missing, rather than
/// just denying gracefully.
/// </summary>
public sealed class IosContinuousSpeechRecognizer : IContinuousSpeechRecognizer
{
    private readonly ILogger<IosContinuousSpeechRecognizer> _logger;

    private SFSpeechRecognizer? _recognizer;
    private SFSpeechAudioBufferRecognitionRequest? _request;
    private SFSpeechRecognitionTask? _task;
    private AVAudioEngine? _audioEngine;
    private bool _isMuted;
    private bool _stoppedIntentionally;

    public IosContinuousSpeechRecognizer(ILogger<IosContinuousSpeechRecognizer> logger)
    {
        _logger = logger;
    }

    public bool IsListening { get; private set; }

    public event EventHandler<string>? PartialResult;
    public event EventHandler<string>? FinalResult;
    public event EventHandler<string>? Error;

    public async Task StartListeningAsync(CancellationToken ct = default)
    {
        var authStatus = await RequestSpeechAuthorizationAsync();
        if (authStatus != SFSpeechRecognizerAuthorizationStatus.Authorized)
        {
            _logger.LogWarning("SFSpeechRecognizer authorization status: {Status}", authStatus);
            Error?.Invoke(this, $"Speech recognition permission was not granted ({authStatus}).");
            return;
        }

        _stoppedIntentionally = false;
        _isMuted = false;
        IsListening = true;
        MainThread.BeginInvokeOnMainThread(BeginSession);
    }

    public Task StopListeningAsync()
    {
        _stoppedIntentionally = true;
        IsListening = false;
        MainThread.BeginInvokeOnMainThread(TeardownSession);
        return Task.CompletedTask;
    }

    public void Mute()
    {
        if (_isMuted) return;
        _isMuted = true;
        // Actually stop capturing (not just discard results) — TTS coordination requires the mic
        // isn't live while the app speaks, per spec §5.3, same reasoning as the Android implementation.
        MainThread.BeginInvokeOnMainThread(TeardownSession);
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
        TeardownSession();

        _recognizer = new SFSpeechRecognizer();
        if (_recognizer is null || !_recognizer.Available)
        {
            _logger.LogWarning("SFSpeechRecognizer is not available on this device/locale.");
            Error?.Invoke(this, "Speech recognition is not available on this device.");
            return;
        }

        try
        {
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSessionCategory.Record, AVAudioSessionCategoryOptions.DuckOthers);
            session.SetActive(true);

            _audioEngine = new AVAudioEngine();
            _request = new SFSpeechAudioBufferRecognitionRequest { ShouldReportPartialResults = true };

            var inputNode = _audioEngine.InputNode;
            var recordingFormat = inputNode.GetBusOutputFormat(0);
            inputNode.InstallTapOnBus(0, 1024, recordingFormat, (buffer, _) => _request?.Append(buffer));

            _audioEngine.Prepare();
            _audioEngine.StartAndReturnError(out var startError);
            if (startError is not null)
            {
                _logger.LogWarning("AVAudioEngine failed to start: {Error}", startError.LocalizedDescription);
                Error?.Invoke(this, $"Audio engine failed to start: {startError.LocalizedDescription}");
                TeardownSession();
                return;
            }

            _task = _recognizer.GetRecognitionTask(_request, HandleRecognitionResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start iOS speech session.");
            Error?.Invoke(this, "Failed to start speech recognition.");
            TeardownSession();
        }
    }

    private void HandleRecognitionResult(SFSpeechRecognitionResult? result, NSError? error)
    {
        if (_isMuted) return;

        if (result is not null)
        {
            var text = result.BestTranscription.FormattedString;
            if (result.Final)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    FinalResult?.Invoke(this, text);
                }
                RestartIfStillListening();
            }
            else if (!string.IsNullOrWhiteSpace(text))
            {
                PartialResult?.Invoke(this, text);
            }
        }

        if (error is not null)
        {
            // SFSpeechRecognizer doesn't cleanly distinguish "routine session end" from a real
            // error the way Android's SpeechRecognizerError enum does — treat all as session-end
            // and restart, same principle as Android's silence-timeout restart loop.
            RestartIfStillListening();
        }
    }

    private void RestartIfStillListening()
    {
        if (_stoppedIntentionally || _isMuted || !IsListening) return;
        MainThread.BeginInvokeOnMainThread(BeginSession);
    }

    private void TeardownSession()
    {
        try
        {
            _task?.Cancel();
            _request?.EndAudio();
            if (_audioEngine is { Running: true })
            {
                _audioEngine.Stop();
                _audioEngine.InputNode.RemoveTapOnBus(0);
            }
            AVAudioSession.SharedInstance().SetActive(false, AVAudioSessionSetActiveOptions.NotifyOthersOnDeactivation, out _);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error tearing down iOS speech session (usually harmless).");
        }
        finally
        {
            _task = null;
            _request = null;
            _audioEngine = null;
            _recognizer = null;
        }
    }

    private static Task<SFSpeechRecognizerAuthorizationStatus> RequestSpeechAuthorizationAsync()
    {
        var current = SFSpeechRecognizer.AuthorizationStatus;
        if (current != SFSpeechRecognizerAuthorizationStatus.NotDetermined)
        {
            return Task.FromResult(current);
        }

        var tcs = new TaskCompletionSource<SFSpeechRecognizerAuthorizationStatus>();
        SFSpeechRecognizer.RequestAuthorization(status => tcs.TrySetResult(status));
        return tcs.Task;
    }
}
