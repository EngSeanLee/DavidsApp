using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DavidsApp.Client.Models;
using DavidsApp.Client.Services;
using DavidsApp.Client.Services.Api;
using DavidsApp.Client.Services.Diagnostics;
using DavidsApp.Client.Services.Speech;
using DavidsApp.Client.Services.StateMachine;
using Microsoft.Extensions.Logging;

namespace DavidsApp.Client.ViewModels;

/// <summary>
/// Drives the hands-free capture screen: wires CaptureStateMachine (the workflow/what-to-call-next
/// logic) to IContinuousSpeechRecognizer (the mic) and ITextToSpeechService (spoken prompts),
/// with CommandWordDetector filtering control phrases out of dictated content first. This is the
/// MAUI-facing orchestration layer — everything it depends on is an interface defined in Core, so
/// the wiring itself doesn't need a MAUI reference (the concrete recognizer/TTS implementations
/// registered in MauiProgram.cs do).
/// </summary>
public sealed partial class CaptureViewModel : ObservableObject
{
    private readonly CaptureStateMachine _stateMachine;
    private readonly IApiClient _api;
    private readonly IContinuousSpeechRecognizer _recognizer;
    private readonly ITextToSpeechService _tts;
    private readonly IDiagnosticLog _diagnosticLog;
    private readonly IUrlLauncher _urlLauncher;
    private readonly ILogger<CaptureViewModel> _logger;

    private bool _pendingCancelConfirmation;

    public CaptureViewModel(
        CaptureStateMachine stateMachine,
        IApiClient api,
        IContinuousSpeechRecognizer recognizer,
        ITextToSpeechService tts,
        IDiagnosticLog diagnosticLog,
        IUrlLauncher urlLauncher,
        ILogger<CaptureViewModel> logger)
    {
        _stateMachine = stateMachine;
        _api = api;
        _recognizer = recognizer;
        _tts = tts;
        _diagnosticLog = diagnosticLog;
        _urlLauncher = urlLauncher;
        _logger = logger;

        _stateMachine.StateChanged += (_, state) => RefreshFromState(state);
        _stateMachine.FindingSaved += (_, row) => LastSavedSummary = Summarize(row);
        _stateMachine.FindingDeleted += (_, row) => LastMessage = $"Deleted finding #{row.Number}.";

        _recognizer.FinalResult += OnFinalResult;
        _recognizer.PartialResult += (_, text) => LiveTranscriptPreview = text;
        _recognizer.Error += async (_, message) =>
        {
            _logger.LogWarning("Speech recognizer error: {Message}", message);
            StatusIndicator = SpeechStateIndicator.SpeechFailed;
            LastMessage = "Speech recognition failed. You can still type findings manually.";
            await LogDiagnosticAsync("recognizer_error", message: message);
        };
    }

    [ObservableProperty]
    public partial SpeechStateIndicator StatusIndicator { get; set; } = SpeechStateIndicator.Ready;

    [ObservableProperty]
    public partial string LastMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LastSavedSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LiveTranscriptPreview { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ManualEntryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMicMuted { get; set; }

    [ObservableProperty]
    public partial bool IsGeneratingReport { get; set; }

    public string PendingRowSummary => _stateMachine.PendingRow is { } row ? Summarize(row) : string.Empty;

    public CaptureState State => _stateMachine.State;

    /// <summary>
    /// Fetches the project's last-saved row (feeds shorthand parsing, e.g. "same reading 1.2",
    /// and restores context after app restart/reselection — see getLastSavedRow in
    /// docs/api-contract.md) before activating capture.
    /// </summary>
    public async Task InitializeAsync(string projectId, CancellationToken ct = default)
    {
        FindingRow? lastSavedRow = null;
        try
        {
            var envelope = await _api.GetLastSavedRowAsync(projectId, ct);
            if (envelope.Status == ApiStatus.Confirm)
            {
                lastSavedRow = envelope.Data?.LastRow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch last saved row for {ProjectId}; continuing without shorthand context.", projectId);
        }

        _stateMachine.SetActiveProject(projectId, lastSavedRow);
        LastSavedSummary = lastSavedRow is not null ? Summarize(lastSavedRow) : string.Empty;

        // Speech hardware/OS setup (no mic, no recognition language installed, permission denied,
        // etc.) must never take down the whole app — manual entry remains a full fallback per
        // spec §5.3, so a recognizer failure here is degraded capability, not a fatal error. This
        // was caught by an actual crash during Windows testing: an unhandled exception here,
        // called from a page's `async void OnAppearing`, is fatal to the whole process.
        try
        {
            await _recognizer.StartListeningAsync(ct);
            StatusIndicator = SpeechStateIndicator.Ready;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start the speech recognizer; continuing with manual entry only.");
            StatusIndicator = SpeechStateIndicator.SpeechFailed;
            LastMessage = "Voice capture isn't available on this device. You can still type findings manually.";
        }
    }

    public async Task ShutdownAsync()
    {
        try
        {
            await _recognizer.StopListeningAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping the speech recognizer during shutdown (non-fatal).");
        }
    }

    [RelayCommand]
    private async Task SubmitManualEntryAsync()
    {
        var text = ManualEntryText;
        ManualEntryText = string.Empty;
        if (!string.IsNullOrWhiteSpace(text))
        {
            await HandleFinalTranscriptAsync(text);
        }
    }

    [RelayCommand]
    private void ToggleMic()
    {
        if (IsMicMuted)
        {
            _recognizer.Unmute();
            IsMicMuted = false;
        }
        else
        {
            _recognizer.Mute();
            IsMicMuted = true;
        }
    }

    // These buttons are always visible/enabled in the current simple layout rather than
    // conditionally shown per state, so each command guards against being invoked from the wrong
    // CaptureState with a spoken/logged no-op instead of letting CaptureStateMachine throw.

    [RelayCommand]
    private Task ConfirmSaveAsync() =>
        _stateMachine.State == CaptureState.Confirm
            ? RunAndSpeakAsync(() => _stateMachine.ConfirmSaveAsync(), "saveFinding")
            : SpeakAsync("Nothing ready to save yet.");

    [RelayCommand]
    private Task DeleteLastAsync() => RunAndSpeakAsync(() => _stateMachine.DeleteLastAsync(), "deleteLastFinding");

    [RelayCommand]
    private void Cancel()
    {
        _stateMachine.Cancel();
        _pendingCancelConfirmation = false;
    }

    [RelayCommand]
    private Task AcceptVocabularyAsync() =>
        _stateMachine.State == CaptureState.UnknownVocabulary
            ? RunAndSpeakAsync(() => _stateMachine.SubmitVocabularyResolutionAsync(accepted: true), "resolveVocabulary")
            : Task.CompletedTask;

    [RelayCommand]
    private Task RejectVocabularyAsync() =>
        _stateMachine.State == CaptureState.UnknownVocabulary
            ? RunAndSpeakAsync(() => _stateMachine.SubmitVocabularyResolutionAsync(accepted: false), "resolveVocabulary")
            : Task.CompletedTask;

    [RelayCommand]
    private async Task RepeatAsync() => await SpeakAsync(string.IsNullOrEmpty(LastMessage) ? "Nothing to repeat." : LastMessage);

    [RelayCommand]
    private async Task GenerateReportAsync()
    {
        var projectId = _stateMachine.ActiveProjectId;
        if (projectId is null || IsGeneratingReport) return;

        IsGeneratingReport = true;
        try
        {
            var envelope = await _api.GenerateReportAsync(projectId);
            LastMessage = envelope.Message;
            await LogDiagnosticAsync("api_call", action: "generateReport", status: envelope.Status.ToString(), errorCode: envelope.ErrorCode, message: envelope.Message);

            if (envelope.Status == ApiStatus.Confirm && !string.IsNullOrEmpty(envelope.Data?.ReportUrl))
            {
                await _urlLauncher.OpenAsync(envelope.Data.ReportUrl);
            }
            else
            {
                await SpeakAsync(string.IsNullOrEmpty(LastMessage) ? "Couldn't generate the report." : LastMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "generateReport failed");
            LastMessage = "Couldn't generate the report.";
        }
        finally
        {
            IsGeneratingReport = false;
        }
    }

    /// <summary>
    /// The single entry point for anything that could be a transcript — real speech or the
    /// manual-entry fallback both funnel through here, so command-word detection and state
    /// routing behave identically regardless of input source.
    /// </summary>
    private async void OnFinalResult(object? sender, string transcript)
    {
        // Raw STT text logged separately from parsed output, per spec §5.3 — isolates
        // recognition errors from parsing errors when debugging a field report. Logged before
        // any parsing/command-detection happens, so it's captured even if handling throws.
        _logger.LogInformation("Raw STT transcript: {Transcript}", transcript);
        await LogDiagnosticAsync("raw_stt", rawTranscript: transcript);
        try
        {
            await HandleFinalTranscriptAsync(transcript);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing transcript: {Transcript}", transcript);
            await LogDiagnosticAsync("unhandled_error", rawTranscript: transcript, message: ex.Message);
        }
    }

    private async Task HandleFinalTranscriptAsync(string transcript)
    {
        var command = CommandWordDetector.Detect(transcript);
        if (command != VoiceCommand.None)
        {
            await LogDiagnosticAsync("voice_command", rawTranscript: transcript, action: command.ToString());
        }

        switch (command)
        {
            case VoiceCommand.Pause:
                _recognizer.Mute();
                IsMicMuted = true;
                await SpeakAsync("Paused.");
                return;

            case VoiceCommand.Resume:
                _recognizer.Unmute();
                IsMicMuted = false;
                await SpeakAsync("Resumed.");
                return;

            case VoiceCommand.Cancel:
                await HandleCancelCommandAsync();
                return;

            case VoiceCommand.Save:
                if (_stateMachine.State == CaptureState.Confirm)
                {
                    await RunAndSpeakAsync(() => _stateMachine.ConfirmSaveAsync(), "saveFinding");
                }
                else
                {
                    await SpeakAsync("Nothing ready to save yet.");
                }
                return;

            case VoiceCommand.Repeat:
                await SpeakAsync(string.IsNullOrEmpty(LastMessage) ? "Nothing to repeat." : LastMessage);
                return;
        }

        _pendingCancelConfirmation = false;

        switch (_stateMachine.State)
        {
            case CaptureState.UnknownVocabulary:
                await HandleVocabularyResponseAsync(transcript);
                break;

            case CaptureState.Idle:
                await RunAndSpeakAsync(() => _stateMachine.SubmitTranscriptAsync(transcript), "parseFinding");
                break;

            case CaptureState.MissingField:
                await RunAndSpeakAsync(() => _stateMachine.SubmitTranscriptAsync(transcript), "resolveMissingField");
                break;

            case CaptureState.Confirm:
                await SpeakAsync("Say save to confirm, or cancel to discard.");
                break;

            case CaptureState.SpeechFailed:
                _stateMachine.Retry();
                break;

            default:
                _logger.LogInformation("Transcript ignored in state {State}: {Transcript}", _stateMachine.State, transcript);
                break;
        }
    }

    private async Task HandleCancelCommandAsync()
    {
        if (_stateMachine.PendingRow is null)
        {
            _stateMachine.Cancel();
            return;
        }

        // Debounce: a populated pendingRow needs a second "cancel" before it's actually
        // discarded, per spec §5.1 ("needs debounce/confirmation before an accidental 'cancel'
        // discards real data").
        if (_pendingCancelConfirmation)
        {
            _stateMachine.Cancel();
            _pendingCancelConfirmation = false;
            await SpeakAsync("Discarded.");
        }
        else
        {
            _pendingCancelConfirmation = true;
            await SpeakAsync("Say cancel again to discard this finding.");
        }
    }

    private async Task HandleVocabularyResponseAsync(string transcript)
    {
        var normalized = transcript.Trim().ToLowerInvariant();
        bool? accepted = normalized switch
        {
            "yes" or "yeah" or "yep" or "correct" or "add it" => true,
            "no" or "nope" or "incorrect" => false,
            _ => null,
        };

        if (accepted is null)
        {
            await SpeakAsync("Sorry, is that a new value? Say yes or no.");
            return;
        }

        await RunAndSpeakAsync(() => _stateMachine.SubmitVocabularyResolutionAsync(accepted.Value), "resolveVocabulary");
    }

    private async Task RunAndSpeakAsync(Func<Task> action, string actionName)
    {
        StatusIndicator = SpeechStateIndicator.Processing;
        await action();
        LastMessage = _stateMachine.LastMessage ?? string.Empty;
        RefreshFromState(_stateMachine.State);

        await LogDiagnosticAsync(
            "api_call",
            action: actionName,
            status: _stateMachine.State.ToString(),
            errorCode: _stateMachine.LastErrorCode,
            pendingRow: _stateMachine.PendingRow,
            lastSavedRow: _stateMachine.LastSavedRow,
            message: LastMessage);

        await SpeakAsync(LastMessage);
    }

    /// <summary>Never throws — a logging failure must not break the interaction it's describing.</summary>
    private async Task LogDiagnosticAsync(
        string eventType,
        string? rawTranscript = null,
        string? action = null,
        string? status = null,
        string? errorCode = null,
        FindingRow? pendingRow = null,
        FindingRow? lastSavedRow = null,
        string? message = null)
    {
        try
        {
            await _diagnosticLog.LogAsync(new DiagnosticLogEntry(
                Timestamp: DateTimeOffset.Now,
                EventType: eventType,
                ProjectId: _stateMachine.ActiveProjectId,
                RawTranscript: rawTranscript,
                Action: action,
                Status: status,
                ErrorCode: errorCode,
                PendingRowSummary: pendingRow is not null ? Summarize(pendingRow) : null,
                LastSavedRowSummary: lastSavedRow is not null ? Summarize(lastSavedRow) : null,
                Message: message));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Diagnostic logging failed (non-fatal).");
        }
    }

    private void RefreshFromState(CaptureState state)
    {
        StatusIndicator = SpeechStateIndicatorMapper.From(state);
        OnPropertyChanged(nameof(PendingRowSummary));
        OnPropertyChanged(nameof(State));
    }

    /// <summary>TTS/STT coordination per spec §5.3: the mic must never listen while the app is
    /// speaking. Mute isn't the same as stopping — listening resumes exactly where it left off.</summary>
    private async Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var wasMuted = IsMicMuted;
        _recognizer.Mute();
        try
        {
            await _tts.SpeakAsync(text);
        }
        finally
        {
            if (!wasMuted)
            {
                _recognizer.Unmute();
            }
        }
    }

    private static string Summarize(FindingRow row) =>
        $"{row.Room} / {row.Wall} / {row.Pos} — {row.Color} {row.Substrate}, {row.State}, {row.Component}: {row.Reading}";
}
