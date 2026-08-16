using DavidsApp.Client.Models;
using DavidsApp.Client.Services.StateMachine;
using Xunit;

namespace DavidsApp.Client.Core.Tests;

public class CaptureStateMachineTests
{
    private static ApiEnvelope<FindingResultData> Confirm(FindingRow pendingRow) => new()
    {
        Status = ApiStatus.Confirm,
        Data = new FindingResultData { PendingRow = pendingRow },
        Message = "ok",
    };

    private static ApiEnvelope<FindingResultData> MissingField(string field, FindingRow pendingRowSoFar) => new()
    {
        Status = ApiStatus.MissingField,
        Data = new FindingResultData { Field = field, PendingRowSoFar = pendingRowSoFar },
        Message = $"missing {field}",
    };

    private static ApiEnvelope<FindingResultData> UnknownValue(string category, string rawValue, FindingRow pendingRowSoFar) => new()
    {
        Status = ApiStatus.UnknownValue,
        Data = new FindingResultData { Category = category, RawValue = rawValue, PendingRowSoFar = pendingRowSoFar },
        Message = $"unknown {category}",
    };

    private static (CaptureStateMachine sm, FakeApiClient api) Make()
    {
        var api = new FakeApiClient();
        var sm = new CaptureStateMachine(api);
        sm.SetActiveProject("proj-1", lastSavedRow: null);
        return (sm, api);
    }

    [Fact]
    public async Task Idle_transcript_calls_parseFinding_and_moves_to_Confirm_on_complete_result()
    {
        var (sm, api) = Make();
        api.OnParseFinding = (_, _, _) => Confirm(new FindingRow { Room = "Kitchen", Reading = "1.2" });

        await sm.SubmitTranscriptAsync("kitchen north trim white wood intact sill 1.2");

        Assert.Equal(["parseFinding(kitchen north trim white wood intact sill 1.2)"], api.CallLog);
        Assert.Equal(CaptureState.Confirm, sm.State);
        Assert.Equal("Kitchen", sm.PendingRow?.Room);
    }

    [Fact]
    public async Task Idle_transcript_with_missing_field_moves_to_MissingField()
    {
        var (sm, api) = Make();
        api.OnParseFinding = (_, _, _) => MissingField("reading", new FindingRow { Room = "Kitchen" });

        await sm.SubmitTranscriptAsync("kitchen north trim white wood intact sill");

        Assert.Equal(CaptureState.MissingField, sm.State);
        Assert.Equal("Kitchen", sm.PendingRow?.Room);
    }

    /// <summary>
    /// THE invariant from docs/state-machine.md: once in MissingField, the next transcript must
    /// route to resolveMissingField, never a fresh parseFinding. Spec calls this out as a
    /// previously hard-learned bug — this test is the regression guard for it.
    /// </summary>
    [Fact]
    public async Task MissingField_next_transcript_routes_to_resolveMissingField_not_parseFinding()
    {
        var (sm, api) = Make();
        api.OnParseFinding = (_, _, _) => MissingField("reading", new FindingRow { Room = "Kitchen" });
        api.OnResolveMissingField = (_, field, value, pendingRow) =>
            Confirm(new FindingRow { Room = pendingRow.Room, Reading = value });

        await sm.SubmitTranscriptAsync("kitchen north trim white wood intact sill");
        Assert.Equal(CaptureState.MissingField, sm.State);

        await sm.SubmitTranscriptAsync("1.2");

        Assert.Equal(
            ["parseFinding(kitchen north trim white wood intact sill)", "resolveMissingField(reading=1.2)"],
            api.CallLog);
        Assert.Equal(CaptureState.Confirm, sm.State);
        Assert.Equal("1.2", sm.PendingRow?.Reading);
    }

    [Fact]
    public async Task Chained_missing_fields_each_route_to_resolveMissingField_never_parseFinding_again()
    {
        var (sm, api) = Make();
        api.OnParseFinding = (_, _, _) => MissingField("room", new FindingRow());
        api.OnResolveMissingField = (_, field, value, pendingRow) => field switch
        {
            "room" => MissingField("reading", new FindingRow { Room = value }),
            "reading" => Confirm(new FindingRow { Room = pendingRow.Room, Reading = value }),
            _ => throw new InvalidOperationException($"unexpected field {field}"),
        };

        await sm.SubmitTranscriptAsync("start");
        await sm.SubmitTranscriptAsync("Kitchen");
        await sm.SubmitTranscriptAsync("1.2");

        Assert.Equal(
            ["parseFinding(start)", "resolveMissingField(room=Kitchen)", "resolveMissingField(reading=1.2)"],
            api.CallLog);
        Assert.Equal(CaptureState.Confirm, sm.State);
    }

    [Fact]
    public async Task SubmitTranscriptAsync_throws_from_Confirm_state()
    {
        var (sm, api) = Make();
        api.OnParseFinding = (_, _, _) => Confirm(new FindingRow { Room = "Kitchen" });
        await sm.SubmitTranscriptAsync("anything");
        Assert.Equal(CaptureState.Confirm, sm.State);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sm.SubmitTranscriptAsync("more"));
    }

    [Fact]
    public async Task UnknownVocabulary_accept_routes_through_resolveVocabulary_to_Confirm()
    {
        var (sm, api) = Make();
        api.OnParseFinding = (_, _, _) => UnknownValue("ROOM", "Kitchen", new FindingRow());
        api.OnResolveVocabulary = (_, category, rawValue, accepted, _, pendingRow) =>
            Confirm(new FindingRow { Room = rawValue });

        await sm.SubmitTranscriptAsync("kitchen finding");
        Assert.Equal(CaptureState.UnknownVocabulary, sm.State);

        await sm.SubmitVocabularyResolutionAsync(accepted: true);

        Assert.Contains("resolveVocabulary(ROOM=Kitchen,accepted=True)", api.CallLog);
        Assert.Equal(CaptureState.Confirm, sm.State);
        Assert.Equal("Kitchen", sm.PendingRow?.Room);
    }

    [Fact]
    public async Task ConfirmSave_persists_row_fires_FindingSaved_and_resets_to_Idle()
    {
        var (sm, api) = Make();
        api.OnParseFinding = (_, _, _) => Confirm(new FindingRow { Room = "Kitchen", Reading = "1.2" });
        api.OnSaveFinding = (_, row) => new ApiEnvelope<SaveFindingData>
        {
            Status = ApiStatus.Confirm,
            Data = new SaveFindingData { SavedRow = new FindingRow { Room = row.Room, Reading = row.Reading, Number = 1 } },
            Message = "saved",
        };

        FindingRow? savedEventRow = null;
        sm.FindingSaved += (_, row) => savedEventRow = row;

        await sm.SubmitTranscriptAsync("kitchen 1.2");
        Assert.Equal(CaptureState.Confirm, sm.State);

        await sm.ConfirmSaveAsync();

        Assert.Equal(CaptureState.Idle, sm.State);
        Assert.Null(sm.PendingRow);
        Assert.NotNull(savedEventRow);
        Assert.Equal(1, savedEventRow!.Number);
        Assert.Equal(1, sm.LastSavedRow?.Number);
    }

    [Fact]
    public async Task ConfirmSaveAsync_throws_when_not_in_Confirm_state()
    {
        var (sm, _) = Make();
        await Assert.ThrowsAsync<InvalidOperationException>(() => sm.ConfirmSaveAsync());
    }

    [Fact]
    public async Task Cancel_from_MissingField_discards_pendingRow_and_returns_to_Idle()
    {
        var (sm, api) = Make();
        api.OnParseFinding = (_, _, _) => MissingField("reading", new FindingRow { Room = "Kitchen" });
        await sm.SubmitTranscriptAsync("kitchen");
        Assert.Equal(CaptureState.MissingField, sm.State);

        sm.Cancel();

        Assert.Equal(CaptureState.Idle, sm.State);
        Assert.Null(sm.PendingRow);
    }

    [Fact]
    public async Task Error_status_enters_SpeechFailed_and_Retry_restores_prior_state_with_context()
    {
        var (sm, api) = Make();
        api.OnParseFinding = (_, _, _) => new ApiEnvelope<FindingResultData> { Status = ApiStatus.Error, ErrorCode = "network_error", Message = "offline" };

        await sm.SubmitTranscriptAsync("kitchen 1.2");

        Assert.Equal(CaptureState.SpeechFailed, sm.State);
        Assert.Equal("network_error", sm.LastErrorCode);

        sm.Retry();

        Assert.Equal(CaptureState.Idle, sm.State);
    }

    [Fact]
    public async Task Error_during_resolveMissingField_preserves_pendingRow_and_missing_field_on_retry()
    {
        var (sm, api) = Make();
        api.OnParseFinding = (_, _, _) => MissingField("reading", new FindingRow { Room = "Kitchen" });
        var callCount = 0;
        api.OnResolveMissingField = (_, _, _, _) =>
        {
            callCount++;
            return callCount == 1
                ? new ApiEnvelope<FindingResultData> { Status = ApiStatus.Error, ErrorCode = "network_error", Message = "offline" }
                : Confirm(new FindingRow { Room = "Kitchen", Reading = "1.2" });
        };

        await sm.SubmitTranscriptAsync("kitchen");
        await sm.SubmitTranscriptAsync("1.2"); // fails
        Assert.Equal(CaptureState.SpeechFailed, sm.State);

        sm.Retry();
        Assert.Equal(CaptureState.MissingField, sm.State);
        Assert.Equal("Kitchen", sm.PendingRow?.Room); // context survived the failure

        await sm.SubmitTranscriptAsync("1.2"); // retry the same input — still routes to resolveMissingField

        Assert.Equal(
            ["parseFinding(kitchen)", "resolveMissingField(reading=1.2)", "resolveMissingField(reading=1.2)"],
            api.CallLog);
        Assert.Equal(CaptureState.Confirm, sm.State);
    }

    [Fact]
    public async Task Pause_then_Resume_restores_state_and_preserves_pendingRow()
    {
        var (sm, api) = Make();
        api.OnParseFinding = (_, _, _) => MissingField("reading", new FindingRow { Room = "Kitchen" });
        await sm.SubmitTranscriptAsync("kitchen");
        Assert.Equal(CaptureState.MissingField, sm.State);

        sm.Pause();
        Assert.Equal(CaptureState.Paused, sm.State);
        Assert.Equal("Kitchen", sm.PendingRow?.Room); // context preserved while paused

        sm.Resume();
        Assert.Equal(CaptureState.MissingField, sm.State);
    }

    [Fact]
    public async Task DeleteLastAsync_fires_FindingDeleted_and_restores_previous_last_row()
    {
        var (sm, api) = Make();
        var previous = new FindingRow { Number = 1, Room = "Bedroom" };
        var deleted = new FindingRow { Number = 2, Room = "Kitchen" };
        api.OnDeleteLastFinding = _ => new ApiEnvelope<DeleteFindingData>
        {
            Status = ApiStatus.Confirm,
            Data = new DeleteFindingData { DeletedRow = deleted, PreviousLastRow = previous },
            Message = "deleted",
        };

        FindingRow? deletedEventRow = null;
        sm.FindingDeleted += (_, row) => deletedEventRow = row;

        await sm.DeleteLastAsync();

        Assert.Equal(CaptureState.Idle, sm.State);
        Assert.Equal(2, deletedEventRow?.Number);
        Assert.Equal(1, sm.LastSavedRow?.Number);
    }
}
