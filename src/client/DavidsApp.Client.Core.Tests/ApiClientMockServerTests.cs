using DavidsApp.Client.Models;
using DavidsApp.Client.Services.Api;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DavidsApp.Client.Core.Tests;

/// <summary>
/// Exercises the real ApiClient (JSON serialization, HTTP, envelope deserialization — everything
/// FakeApiClient in CaptureStateMachineTests bypasses) against a genuinely running tools/mock-api
/// instance. Requires `node tools/mock-api/server.js` running on the default port first; skips
/// cleanly (doesn't fail the suite) if nothing is listening, so `dotnet test` alone stays green
/// in CI/a fresh checkout.
/// </summary>
public class ApiClientMockServerTests : IAsyncLifetime
{
    private readonly ApiClientOptions _options = new();
    private ApiClient _client = null!;
    private bool _serverReachable;

    public async Task InitializeAsync()
    {
        _client = new ApiClient(new HttpClient(), _options, NullLogger<ApiClient>.Instance);
        try
        {
            var probe = await _client.ListProjectsAsync();
            _serverReachable = probe.ErrorCode != "network_error";
        }
        catch
        {
            _serverReachable = false;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task StartProject_then_saveFinding_round_trip_through_real_json_wire_format()
    {
        Skip.IfNot(_serverReachable, "tools/mock-api is not running on localhost:4127 — start it with `node tools/mock-api/server.js` to run this test.");

        var started = await _client.StartProjectAsync("123 Integration Test Ave", null, "JOB-IT");
        Assert.Equal(ApiStatus.Confirm, started.Status);
        Assert.NotNull(started.Data);
        var projectId = started.Data!.ProjectId;
        Assert.NotEmpty(projectId);

        var parsed = await _client.ParseFindingAsync(projectId, "kitchen finding", null);
        Assert.Equal(ApiStatus.Confirm, parsed.Status);
        Assert.NotNull(parsed.Data?.PendingRow);
        Assert.Equal("Kitchen", parsed.Data!.PendingRow!.Room);

        var saved = await _client.SaveFindingAsync(projectId, parsed.Data.PendingRow!);
        Assert.Equal(ApiStatus.Confirm, saved.Status);
        Assert.Equal(1, saved.Data?.SavedRow?.Number);

        var missing = await _client.ParseFindingAsync(projectId, "this is missing something", null);
        Assert.Equal(ApiStatus.MissingField, missing.Status);
        Assert.Equal("reading", missing.Data?.Field);

        var unauthorized = await new ApiClient(new HttpClient(), new ApiClientOptions { ApiKey = "wrong-key" }, NullLogger<ApiClient>.Instance)
            .ListProjectsAsync();
        Assert.Equal(ApiStatus.Error, unauthorized.Status);
        Assert.Equal("unauthorized", unauthorized.ErrorCode);
    }
}
