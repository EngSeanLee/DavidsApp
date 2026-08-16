using DavidsApp.Client.Models;
using DavidsApp.Client.Services.StateMachine;
using DavidsApp.Client.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DavidsApp.Client.Core.Tests;

public class CaptureViewModelTests
{
    private static (CaptureViewModel vm, FakeApiClient api, FakeSpeechRecognizer recognizer, FakeTextToSpeechService tts, FakeDiagnosticLog log) Make()
    {
        var api = new FakeApiClient();
        var stateMachine = new CaptureStateMachine(api);
        var recognizer = new FakeSpeechRecognizer();
        var tts = new FakeTextToSpeechService();
        var log = new FakeDiagnosticLog();
        var vm = new CaptureViewModel(stateMachine, api, recognizer, tts, log, NullLogger<CaptureViewModel>.Instance);
        return (vm, api, recognizer, tts, log);
    }

    /// <summary>
    /// Regression guard for a real crash found during manual Windows testing: a speech recognizer
    /// that fails to start (no mic, no configured recognition, permission denied, etc.) must never
    /// throw out of InitializeAsync — that method gets awaited from a page's `async void
    /// OnAppearing`, where an unhandled exception is fatal to the whole process, not just the screen.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_recognizer_start_failure_degrades_gracefully_instead_of_throwing()
    {
        var (vm, _, recognizer, _, _) = Make();
        recognizer.ThrowOnStart = true;

        var exception = await Record.ExceptionAsync(() => vm.InitializeAsync("proj-1"));

        Assert.Null(exception);
        Assert.Equal(SpeechStateIndicator.SpeechFailed, vm.StatusIndicator);
        Assert.Contains("manually", vm.LastMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeAsync_happy_path_starts_listening()
    {
        var (vm, _, recognizer, _, _) = Make();

        await vm.InitializeAsync("proj-1");

        Assert.True(recognizer.IsListening);
        Assert.Equal(SpeechStateIndicator.Ready, vm.StatusIndicator);
    }

    [Fact]
    public async Task Raw_transcript_is_diagnostic_logged_before_processing()
    {
        var (vm, api, recognizer, _, log) = Make();
        api.OnParseFinding = (_, _, _) => new ApiEnvelope<FindingResultData>
        {
            Status = ApiStatus.Confirm,
            Data = new FindingResultData { PendingRow = new FindingRow { Room = "Kitchen" } },
            Message = "ok",
        };
        await vm.InitializeAsync("proj-1");

        recognizer.RaiseFinalResult("kitchen finding");
        await WaitForAsync(() => log.Entries.Any(e => e.EventType == "raw_stt"));

        var rawEntry = log.Entries.First(e => e.EventType == "raw_stt");
        Assert.Equal("kitchen finding", rawEntry.RawTranscript);
    }

    [Fact]
    public async Task Voice_pause_command_mutes_recognizer_without_reaching_state_machine()
    {
        var (vm, api, recognizer, tts, _) = Make();
        api.OnParseFinding = (_, _, _) => throw new InvalidOperationException("parseFinding should not be called for a command word.");
        await vm.InitializeAsync("proj-1");

        recognizer.RaiseFinalResult("pause");
        await WaitForAsync(() => recognizer.MuteCallCount > 0);

        Assert.True(vm.IsMicMuted);
        Assert.Contains("Paused.", tts.Spoken);
    }

    [Fact]
    public async Task Manual_entry_and_voice_share_the_same_routing_pipeline()
    {
        var (vm, api, _, _, _) = Make();
        var parseFindingCalls = 0;
        api.OnParseFinding = (_, transcript, _) =>
        {
            parseFindingCalls++;
            return new ApiEnvelope<FindingResultData>
            {
                Status = ApiStatus.Confirm,
                Data = new FindingResultData { PendingRow = new FindingRow { Room = "Kitchen", Reading = transcript } },
                Message = "ok",
            };
        };
        await vm.InitializeAsync("proj-1");

        vm.ManualEntryText = "kitchen 1.2";
        await vm.SubmitManualEntryCommand.ExecuteAsync(null);

        Assert.Equal(1, parseFindingCalls);
        Assert.Equal(string.Empty, vm.ManualEntryText); // cleared after send
        Assert.Contains("Kitchen", vm.PendingRowSummary);
    }

    [Fact]
    public async Task Cancel_command_requires_confirmation_when_pendingRow_is_populated()
    {
        var (vm, api, recognizer, tts, _) = Make();
        api.OnParseFinding = (_, _, _) => new ApiEnvelope<FindingResultData>
        {
            Status = ApiStatus.Confirm,
            Data = new FindingResultData { PendingRow = new FindingRow { Room = "Kitchen" } },
            Message = "ok",
        };
        await vm.InitializeAsync("proj-1");
        recognizer.RaiseFinalResult("kitchen finding");
        await WaitForAsync(() => vm.State == CaptureState.Confirm);

        recognizer.RaiseFinalResult("cancel");
        await WaitForAsync(() => tts.Spoken.Contains("Say cancel again to discard this finding."));
        Assert.Equal(CaptureState.Confirm, vm.State); // not discarded yet

        recognizer.RaiseFinalResult("cancel");
        await WaitForAsync(() => vm.State == CaptureState.Idle);
        Assert.Contains("Discarded.", tts.Spoken);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 1000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
        Assert.True(condition(), "Condition was not met within the timeout — likely an async fire-and-forget path (OnFinalResult is `async void`) not completing as expected.");
    }
}
