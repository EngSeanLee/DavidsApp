using DavidsApp.Client.Models;
using DavidsApp.Client.Services.Api;

namespace DavidsApp.Client.Core.Tests;

/// <summary>
/// A hand-written fake (not a mocking-framework double) so CaptureStateMachine tests have zero
/// extra dependencies. Each action has a settable delegate defaulting to "unconfigured" — tests
/// wire up only what the scenario needs, and CallLog records which actions actually fired, in
/// order, which is what the missing-field-routing invariant tests assert on.
/// </summary>
public sealed class FakeApiClient : IApiClient
{
    public List<string> CallLog { get; } = new();

    public Func<string, string, FindingRow?, ApiEnvelope<FindingResultData>>? OnParseFinding { get; set; }
    public Func<string, string, string, FindingRow, ApiEnvelope<FindingResultData>>? OnResolveMissingField { get; set; }
    public Func<string, string, string, bool, string?, FindingRow, ApiEnvelope<FindingResultData>>? OnResolveVocabulary { get; set; }
    public Func<string, FindingRow, ApiEnvelope<SaveFindingData>>? OnSaveFinding { get; set; }
    public Func<string, ApiEnvelope<DeleteFindingData>>? OnDeleteLastFinding { get; set; }

    public Task<ApiEnvelope<Project>> StartProjectAsync(string testingAddress, string? testingDate, string? jobNumber, CancellationToken ct = default) =>
        throw new NotImplementedException("Not needed by CaptureStateMachine tests.");

    public Task<ApiEnvelope<ListProjectsData>> ListProjectsAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("Not needed by CaptureStateMachine tests.");

    public Task<ApiEnvelope<LastSavedRowData>> GetLastSavedRowAsync(string projectId, CancellationToken ct = default) =>
        throw new NotImplementedException("Not needed by CaptureStateMachine tests.");

    public Task<ApiEnvelope<FindingResultData>> ParseFindingAsync(string projectId, string transcript, FindingRow? lastRow, CancellationToken ct = default)
    {
        CallLog.Add($"parseFinding({transcript})");
        if (OnParseFinding is null) throw new InvalidOperationException("OnParseFinding not configured.");
        return Task.FromResult(OnParseFinding(projectId, transcript, lastRow));
    }

    public Task<ApiEnvelope<FindingResultData>> ResolveMissingFieldAsync(string projectId, string field, string value, FindingRow pendingRowSoFar, CancellationToken ct = default)
    {
        CallLog.Add($"resolveMissingField({field}={value})");
        if (OnResolveMissingField is null) throw new InvalidOperationException("OnResolveMissingField not configured.");
        return Task.FromResult(OnResolveMissingField(projectId, field, value, pendingRowSoFar));
    }

    public Task<ApiEnvelope<FindingResultData>> ResolveVocabularyAsync(string projectId, string category, string rawValue, bool accepted, string? normalizedValue, FindingRow pendingRowSoFar, CancellationToken ct = default)
    {
        CallLog.Add($"resolveVocabulary({category}={rawValue},accepted={accepted})");
        if (OnResolveVocabulary is null) throw new InvalidOperationException("OnResolveVocabulary not configured.");
        return Task.FromResult(OnResolveVocabulary(projectId, category, rawValue, accepted, normalizedValue, pendingRowSoFar));
    }

    public Task<ApiEnvelope<SaveFindingData>> SaveFindingAsync(string projectId, FindingRow row, CancellationToken ct = default)
    {
        CallLog.Add("saveFinding");
        if (OnSaveFinding is null) throw new InvalidOperationException("OnSaveFinding not configured.");
        return Task.FromResult(OnSaveFinding(projectId, row));
    }

    public Task<ApiEnvelope<DeleteFindingData>> DeleteLastFindingAsync(string projectId, CancellationToken ct = default)
    {
        CallLog.Add("deleteLastFinding");
        if (OnDeleteLastFinding is null) throw new InvalidOperationException("OnDeleteLastFinding not configured.");
        return Task.FromResult(OnDeleteLastFinding(projectId));
    }

    public Task<ApiEnvelope<ReportData>> GenerateReportAsync(string projectId, CancellationToken ct = default) =>
        throw new NotImplementedException("Not needed by CaptureStateMachine tests.");
}
