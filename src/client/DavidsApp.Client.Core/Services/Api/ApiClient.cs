using System.Net.Http.Json;
using System.Text.Json;
using DavidsApp.Client.Models;
using Microsoft.Extensions.Logging;

namespace DavidsApp.Client.Services.Api;

/// <summary>
/// Posts a single JSON envelope ({apiKey, action, payload}) to a single endpoint for every action,
/// matching docs/api-contract.md. Never throws for a failed/unreachable call — network and
/// deserialization failures both come back as a Status.Error envelope, so callers (the state
/// machine) have one code path for "the backend said no" and "the backend was unreachable."
/// </summary>
public sealed class ApiClient : IApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ApiClientOptions _options;
    private readonly ILogger<ApiClient> _logger;

    public ApiClient(HttpClient http, ApiClientOptions options, ILogger<ApiClient> logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
        _http.BaseAddress = new Uri(_options.BaseUrl);
    }

    public Task<ApiEnvelope<Project>> StartProjectAsync(string testingAddress, string? testingDate, string? jobNumber, CancellationToken ct = default) =>
        PostAsync<StartProjectPayload, Project>("startProject", new StartProjectPayload { TestingAddress = testingAddress, TestingDate = testingDate, JobNumber = jobNumber }, ct);

    public Task<ApiEnvelope<ListProjectsData>> ListProjectsAsync(CancellationToken ct = default) =>
        PostAsync<object, ListProjectsData>("listProjects", new { }, ct);

    public Task<ApiEnvelope<LastSavedRowData>> GetLastSavedRowAsync(string projectId, CancellationToken ct = default) =>
        PostAsync<ProjectScopedPayload, LastSavedRowData>("getLastSavedRow", new ProjectScopedPayload { ProjectId = projectId }, ct);

    public Task<ApiEnvelope<FindingResultData>> ParseFindingAsync(string projectId, string transcript, FindingRow? lastRow, CancellationToken ct = default) =>
        PostAsync<ParseFindingPayload, FindingResultData>("parseFinding", new ParseFindingPayload { ProjectId = projectId, Transcript = transcript, LastRow = lastRow }, ct);

    public Task<ApiEnvelope<FindingResultData>> ResolveMissingFieldAsync(string projectId, string field, string value, FindingRow pendingRowSoFar, CancellationToken ct = default) =>
        PostAsync<ResolveMissingFieldPayload, FindingResultData>("resolveMissingField", new ResolveMissingFieldPayload { ProjectId = projectId, Field = field, Value = value, PendingRowSoFar = pendingRowSoFar }, ct);

    public Task<ApiEnvelope<FindingResultData>> ResolveVocabularyAsync(string projectId, string category, string rawValue, bool accepted, string? normalizedValue, FindingRow pendingRowSoFar, CancellationToken ct = default) =>
        PostAsync<ResolveVocabularyPayload, FindingResultData>("resolveVocabulary", new ResolveVocabularyPayload { ProjectId = projectId, Category = category, RawValue = rawValue, Accepted = accepted, NormalizedValue = normalizedValue, PendingRowSoFar = pendingRowSoFar }, ct);

    public Task<ApiEnvelope<SaveFindingData>> SaveFindingAsync(string projectId, FindingRow row, CancellationToken ct = default) =>
        PostAsync<SaveFindingPayload, SaveFindingData>("saveFinding", new SaveFindingPayload { ProjectId = projectId, Row = row }, ct);

    public Task<ApiEnvelope<DeleteFindingData>> DeleteLastFindingAsync(string projectId, CancellationToken ct = default) =>
        PostAsync<ProjectScopedPayload, DeleteFindingData>("deleteLastFinding", new ProjectScopedPayload { ProjectId = projectId }, ct);

    public Task<ApiEnvelope<ReportData>> GenerateReportAsync(string projectId, CancellationToken ct = default) =>
        PostAsync<ProjectScopedPayload, ReportData>("generateReport", new ProjectScopedPayload { ProjectId = projectId }, ct);

    private async Task<ApiEnvelope<TData>> PostAsync<TPayload, TData>(string action, TPayload payload, CancellationToken ct)
    {
        var request = new ApiRequest<TPayload> { ApiKey = _options.ApiKey, Action = action, Payload = payload };
        try
        {
            using var response = await _http.PostAsJsonAsync(string.Empty, request, JsonOptions, ct);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<TData>>(JsonOptions, ct);
            if (envelope is null)
            {
                _logger.LogWarning("{Action} returned an empty/unparseable body (HTTP {StatusCode})", action, (int)response.StatusCode);
                return ErrorEnvelope<TData>("empty_response", "The server returned an empty response.");
            }
            return envelope;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller-requested cancellation, not a failure — let it propagate normally
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Action} failed", action);
            return ErrorEnvelope<TData>("network_error", "Couldn't reach the server. Check your connection and try again.");
        }
    }

    private static ApiEnvelope<TData> ErrorEnvelope<TData>(string errorCode, string message) =>
        new() { Status = ApiStatus.Error, ErrorCode = errorCode, Message = message };
}
