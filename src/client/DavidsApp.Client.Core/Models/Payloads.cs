using System.Text.Json.Serialization;

namespace DavidsApp.Client.Models;

/// <summary>Action-specific `payload` shapes sent in requests — see docs/api-contract.md "Actions".</summary>

public sealed class StartProjectPayload
{
    [JsonPropertyName("testingAddress")]
    public required string TestingAddress { get; init; }

    [JsonPropertyName("testingDate")]
    public string? TestingDate { get; init; }

    [JsonPropertyName("jobNumber")]
    public string? JobNumber { get; init; }
}

public sealed class ProjectScopedPayload
{
    [JsonPropertyName("projectId")]
    public required string ProjectId { get; init; }
}

public sealed class ParseFindingPayload
{
    [JsonPropertyName("projectId")]
    public required string ProjectId { get; init; }

    [JsonPropertyName("transcript")]
    public required string Transcript { get; init; }

    [JsonPropertyName("lastRow")]
    public FindingRow? LastRow { get; init; }
}

public sealed class ResolveMissingFieldPayload
{
    [JsonPropertyName("projectId")]
    public required string ProjectId { get; init; }

    [JsonPropertyName("field")]
    public required string Field { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("pendingRowSoFar")]
    public required FindingRow PendingRowSoFar { get; init; }
}

public sealed class ResolveVocabularyPayload
{
    [JsonPropertyName("projectId")]
    public required string ProjectId { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("rawValue")]
    public required string RawValue { get; init; }

    [JsonPropertyName("accepted")]
    public required bool Accepted { get; init; }

    [JsonPropertyName("normalizedValue")]
    public string? NormalizedValue { get; init; }

    [JsonPropertyName("pendingRowSoFar")]
    public required FindingRow PendingRowSoFar { get; init; }
}

public sealed class SaveFindingPayload
{
    [JsonPropertyName("projectId")]
    public required string ProjectId { get; init; }

    [JsonPropertyName("row")]
    public required FindingRow Row { get; init; }
}
