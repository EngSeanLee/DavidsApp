using DavidsApp.Client.Models;

namespace DavidsApp.Client.Services.Api;

/// <summary>
/// One method per action in docs/api-contract.md. Implementations POST a single JSON envelope to
/// a single endpoint — see ApiClient. A mock implementation (tools/mock-api, hit over HTTP) lets
/// the rest of the client be built before the real Apps Script deployment, or an OpenAI key, exist.
/// </summary>
public interface IApiClient
{
    Task<ApiEnvelope<Project>> StartProjectAsync(string testingAddress, string? testingDate, string? jobNumber, CancellationToken ct = default);

    Task<ApiEnvelope<ListProjectsData>> ListProjectsAsync(CancellationToken ct = default);

    Task<ApiEnvelope<LastSavedRowData>> GetLastSavedRowAsync(string projectId, CancellationToken ct = default);

    Task<ApiEnvelope<FindingResultData>> ParseFindingAsync(string projectId, string transcript, FindingRow? lastRow, CancellationToken ct = default);

    Task<ApiEnvelope<FindingResultData>> ResolveMissingFieldAsync(string projectId, string field, string value, FindingRow pendingRowSoFar, CancellationToken ct = default);

    Task<ApiEnvelope<FindingResultData>> ResolveVocabularyAsync(string projectId, string category, string rawValue, bool accepted, string? normalizedValue, FindingRow pendingRowSoFar, CancellationToken ct = default);

    Task<ApiEnvelope<SaveFindingData>> SaveFindingAsync(string projectId, FindingRow row, CancellationToken ct = default);

    Task<ApiEnvelope<DeleteFindingData>> DeleteLastFindingAsync(string projectId, CancellationToken ct = default);

    Task<ApiEnvelope<ReportData>> GenerateReportAsync(string projectId, CancellationToken ct = default);
}
