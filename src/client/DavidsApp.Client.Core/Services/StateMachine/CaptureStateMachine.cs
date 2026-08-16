using DavidsApp.Client.Models;
using DavidsApp.Client.Services.Api;

namespace DavidsApp.Client.Services.StateMachine;

/// <summary>
/// Drives the UX state model in docs/state-machine.md. Deliberately independent of speech/UI —
/// takes plain strings (transcripts, resolved values) in and calls IApiClient, so it's unit
/// testable with a fake IApiClient and no MAUI/speech dependencies at all.
///
/// The one rule that must never regress (spec calls this out as a previously hard-learned bug):
/// while in MissingField, the next input goes to ResolveMissingField, never a fresh ParseFinding.
/// </summary>
public sealed class CaptureStateMachine
{
    private readonly IApiClient _api;

    private CaptureState _stateBeforeInterruption = CaptureState.Idle;
    private string? _missingField;
    private string? _unknownCategory;
    private string? _unknownRawValue;

    public CaptureStateMachine(IApiClient api)
    {
        _api = api;
    }

    public CaptureState State { get; private set; } = CaptureState.Idle;

    public string? ActiveProjectId { get; private set; }

    /// <summary>The finding under construction. Null only in Idle/Paused-from-Idle.</summary>
    public FindingRow? PendingRow { get; private set; }

    /// <summary>Most recently saved row for this project — feeds shorthand parsing ("same reading 1.2").</summary>
    public FindingRow? LastSavedRow { get; private set; }

    public string? LastMessage { get; private set; }
    public string? LastErrorCode { get; private set; }

    public event EventHandler<CaptureState>? StateChanged;
    public event EventHandler<FindingRow>? FindingSaved;
    public event EventHandler<FindingRow>? FindingDeleted;

    public void SetActiveProject(string projectId, FindingRow? lastSavedRow)
    {
        ActiveProjectId = projectId;
        LastSavedRow = lastSavedRow;
        PendingRow = null;
        _missingField = null;
        _unknownCategory = null;
        _unknownRawValue = null;
        SetState(CaptureState.Idle);
    }

    /// <summary>
    /// Routes a transcript to the correct action for the current state. This is the routing
    /// invariant: Idle -> parseFinding, MissingField -> resolveMissingField. Any other state
    /// throws — callers should route to SubmitVocabularyResolutionAsync/ConfirmSaveAsync instead.
    /// </summary>
    public async Task SubmitTranscriptAsync(string transcript, CancellationToken ct = default)
    {
        RequireActiveProject();

        switch (State)
        {
            case CaptureState.Idle:
                SetState(CaptureState.ListeningParsing);
                await ApplyFindingResultAsync(
                    await _api.ParseFindingAsync(ActiveProjectId!, transcript, LastSavedRow, ct),
                    fallbackState: CaptureState.Idle);
                break;

            case CaptureState.MissingField:
                if (_missingField is null || PendingRow is null)
                {
                    throw new InvalidOperationException("In MissingField state with no missing field/pendingRow recorded — state machine invariant violated.");
                }
                var field = _missingField;
                var pendingRow = PendingRow;
                await ApplyFindingResultAsync(
                    await _api.ResolveMissingFieldAsync(ActiveProjectId!, field, transcript, pendingRow, ct),
                    fallbackState: CaptureState.MissingField);
                break;

            default:
                throw new InvalidOperationException($"SubmitTranscriptAsync is not valid from state {State}. Use SubmitVocabularyResolutionAsync or ConfirmSaveAsync instead.");
        }
    }

    /// <summary>Valid only from UnknownVocabulary — the user's yes/no (+ optional correction) response.</summary>
    public async Task SubmitVocabularyResolutionAsync(bool accepted, string? normalizedValue = null, CancellationToken ct = default)
    {
        RequireActiveProject();
        if (State != CaptureState.UnknownVocabulary || _unknownCategory is null || _unknownRawValue is null || PendingRow is null)
        {
            throw new InvalidOperationException($"SubmitVocabularyResolutionAsync is not valid from state {State}.");
        }

        var category = _unknownCategory;
        var rawValue = _unknownRawValue;
        var pendingRow = PendingRow;
        await ApplyFindingResultAsync(
            await _api.ResolveVocabularyAsync(ActiveProjectId!, category, rawValue, accepted, normalizedValue, pendingRow, ct),
            fallbackState: CaptureState.UnknownVocabulary);
    }

    /// <summary>Valid only from Confirm — persists PendingRow and resets to Idle.</summary>
    public async Task ConfirmSaveAsync(CancellationToken ct = default)
    {
        RequireActiveProject();
        if (State != CaptureState.Confirm || PendingRow is null)
        {
            throw new InvalidOperationException($"ConfirmSaveAsync is not valid from state {State}.");
        }

        var envelope = await _api.SaveFindingAsync(ActiveProjectId!, PendingRow, ct);
        LastMessage = envelope.Message;
        LastErrorCode = envelope.ErrorCode;

        if (envelope.Status == ApiStatus.Confirm && envelope.Data?.SavedRow is not null)
        {
            var savedRow = envelope.Data.SavedRow;
            LastSavedRow = savedRow;
            PendingRow = null;
            _missingField = null;
            _unknownCategory = null;
            _unknownRawValue = null;
            FindingSaved?.Invoke(this, savedRow);
            SetState(CaptureState.Idle);
        }
        else if (envelope.Status is ApiStatus.MissingField or ApiStatus.UnknownValue)
        {
            // Defensive: saveFinding validates too (e.g. a value became stale between resolution
            // and save). Route back into the normal missing-field/unknown-vocabulary flow — same
            // fields as FindingResultData, just carried on SaveFindingData — rather than losing the row.
            var data = envelope.Data is null
                ? null
                : new FindingResultData
                {
                    Field = envelope.Data.Field,
                    PendingRowSoFar = envelope.Data.PendingRowSoFar,
                    Category = envelope.Data.Category,
                    RawValue = envelope.Data.RawValue,
                };
            ApplyFindingResult(envelope.Status, data, fallbackState: CaptureState.Confirm);
        }
        else
        {
            EnterFailedState(CaptureState.Confirm);
        }
    }

    /// <summary>Deletes the most recently saved finding for the active project (undo).</summary>
    public async Task DeleteLastAsync(CancellationToken ct = default)
    {
        RequireActiveProject();
        var envelope = await _api.DeleteLastFindingAsync(ActiveProjectId!, ct);
        LastMessage = envelope.Message;
        LastErrorCode = envelope.ErrorCode;

        if (envelope.Status == ApiStatus.Confirm && envelope.Data is not null)
        {
            LastSavedRow = envelope.Data.PreviousLastRow;
            FindingDeleted?.Invoke(this, envelope.Data.DeletedRow);
            SetState(CaptureState.Idle);
        }
        else
        {
            EnterFailedState(State);
        }
    }

    /// <summary>"cancel" / "scratch that" — discards the in-progress finding. Debounce/confirmation
    /// before calling this on a populated PendingRow is a UI-layer concern, not this class's job.</summary>
    public void Cancel()
    {
        PendingRow = null;
        _missingField = null;
        _unknownCategory = null;
        _unknownRawValue = null;
        SetState(CaptureState.Idle);
    }

    /// <summary>OS suspended the mic — freeze in place, remembering what to resume into.</summary>
    public void Pause()
    {
        if (State == CaptureState.Paused) return;
        _stateBeforeInterruption = State;
        SetState(CaptureState.Paused);
    }

    public void Resume()
    {
        if (State != CaptureState.Paused) return;
        SetState(_stateBeforeInterruption);
    }

    /// <summary>Retry after SpeechFailed — returns to whatever state preceded the failed call, with pendingRow/missingField context intact.</summary>
    public void Retry()
    {
        if (State != CaptureState.SpeechFailed) return;
        SetState(_stateBeforeInterruption);
    }

    private async Task ApplyFindingResultAsync(ApiEnvelope<FindingResultData> envelope, CaptureState fallbackState)
    {
        LastMessage = envelope.Message;
        LastErrorCode = envelope.ErrorCode;
        ApplyFindingResult(envelope.Status, envelope.Data, fallbackState);
        await Task.CompletedTask;
    }

    private void ApplyFindingResult(ApiStatus status, FindingResultData? data, CaptureState fallbackState)
    {
        switch (status)
        {
            case ApiStatus.Confirm when data?.PendingRow is not null:
                PendingRow = data.PendingRow;
                _missingField = null;
                _unknownCategory = null;
                _unknownRawValue = null;
                SetState(CaptureState.Confirm);
                break;

            case ApiStatus.MissingField when data?.Field is not null:
                PendingRow = data.PendingRowSoFar ?? PendingRow;
                _missingField = data.Field;
                _unknownCategory = null;
                _unknownRawValue = null;
                SetState(CaptureState.MissingField);
                break;

            case ApiStatus.UnknownValue when data?.Category is not null && data.RawValue is not null:
                PendingRow = data.PendingRowSoFar ?? PendingRow;
                _unknownCategory = data.Category;
                _unknownRawValue = data.RawValue;
                SetState(CaptureState.UnknownVocabulary);
                break;

            default:
                EnterFailedState(fallbackState);
                break;
        }
    }

    private void EnterFailedState(CaptureState stateToRetryInto)
    {
        _stateBeforeInterruption = stateToRetryInto;
        SetState(CaptureState.SpeechFailed);
    }

    private void RequireActiveProject()
    {
        if (ActiveProjectId is null)
        {
            throw new InvalidOperationException("No active project — call SetActiveProject first.");
        }
    }

    private void SetState(CaptureState newState)
    {
        State = newState;
        StateChanged?.Invoke(this, newState);
    }
}
