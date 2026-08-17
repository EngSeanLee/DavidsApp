using DavidsApp.Client.Models;
using DavidsApp.Client.Services.StateMachine;
using DavidsApp.Client.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DavidsApp.Client.Core.Tests;

public class CaptureViewModelTests
{
    private static (CaptureViewModel vm, FakeApiClient api, FakeSpeechRecognizer recognizer, FakeTextToSpeechService tts, FakeDiagnosticLog log, FakeUrlLauncher launcher, FakePermissionRequester permissions) Make()
    {
        var api = new FakeApiClient();
        var stateMachine = new CaptureStateMachine(api);
        var recognizer = new FakeSpeechRecognizer();
        var tts = new FakeTextToSpeechService();
        var log = new FakeDiagnosticLog();
        var launcher = new FakeUrlLauncher();
        var permissions = new FakePermissionRequester();
        var vm = new CaptureViewModel(stateMachine, api, recognizer, tts, log, launcher, permissions, NullLogger<CaptureViewModel>.Instance);
        return (vm, api, recognizer, tts, log, launcher, permissions);
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
        var (vm, _, recognizer, _, _, _, _) = Make();
        recognizer.ThrowOnStart = true;

        var exception = await Record.ExceptionAsync(() => vm.InitializeAsync("proj-1"));

        Assert.Null(exception);
        Assert.Equal(SpeechStateIndicator.SpeechFailed, vm.StatusIndicator);
        Assert.Contains("manually", vm.LastMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeAsync_happy_path_starts_listening()
    {
        var (vm, _, recognizer, _, _, _, _) = Make();

        await vm.InitializeAsync("proj-1");

        Assert.True(recognizer.IsListening);
        Assert.Equal(SpeechStateIndicator.Ready, vm.StatusIndicator);
    }

    /// <summary>
    /// Regression guard for a real gap found before first real-device testing: nothing previously
    /// requested the Android RECORD_AUDIO runtime permission anywhere, so the mic would silently
    /// never work even with the manifest entry present. This confirms the app now asks, and
    /// degrades to manual entry (not a crash, not a silent no-op) when denied.
    /// </summary>
    [Fact]
    public async Task InitializeAsync_microphone_permission_denied_degrades_without_starting_recognizer()
    {
        var (vm, _, recognizer, _, _, _, permissions) = Make();
        permissions.MicrophoneGranted = false;

        await vm.InitializeAsync("proj-1");

        Assert.False(recognizer.IsListening);
        Assert.Equal(SpeechStateIndicator.SpeechFailed, vm.StatusIndicator);
        Assert.Contains("manually", vm.LastMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Raw_transcript_is_diagnostic_logged_before_processing()
    {
        var (vm, api, recognizer, _, log, _, _) = Make();
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
        var (vm, api, recognizer, tts, _, _, _) = Make();
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
        var (vm, api, _, _, _, _, _) = Make();
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
        var (vm, api, recognizer, tts, _, _, _) = Make();
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

    [Fact]
    public async Task GenerateReport_opens_the_returned_url_on_success()
    {
        var (vm, api, _, _, log, launcher, _) = Make();
        api.OnGenerateReport = _ => new ApiEnvelope<ReportData>
        {
            Status = ApiStatus.Confirm,
            Data = new ReportData { ReportUrl = "https://drive.google.com/file/d/abc123/view" },
            Message = "Report generated for 123 Main St.",
        };
        await vm.InitializeAsync("proj-1");

        await vm.GenerateReportCommand.ExecuteAsync(null);

        Assert.Equal(["https://drive.google.com/file/d/abc123/view"], launcher.OpenedUrls);
        Assert.False(vm.IsGeneratingReport);
        Assert.Contains(log.Entries, e => e.Action == "generateReport" && e.Status == "Confirm");
    }

    [Fact]
    public async Task GenerateReport_failure_does_not_open_a_url()
    {
        var (vm, api, _, _, _, launcher, _) = Make();
        api.OnGenerateReport = _ => new ApiEnvelope<ReportData>
        {
            Status = ApiStatus.Error,
            ErrorCode = "not_found",
            Message = "Unknown projectId.",
        };
        await vm.InitializeAsync("proj-1");

        await vm.GenerateReportCommand.ExecuteAsync(null);

        Assert.Empty(launcher.OpenedUrls);
        Assert.Equal("Unknown projectId.", vm.LastMessage);
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
