using System.Text.Json.Serialization;

namespace DavidsApp.Client.Models;

/// <summary>Action-specific `data` shapes — see docs/api-contract.md "Actions" for each one.</summary>

public sealed class ListProjectsData
{
    [JsonPropertyName("projects")]
    public List<Project> Projects { get; set; } = new();
}

public sealed class LastSavedRowData
{
    [JsonPropertyName("lastRow")]
    public FindingRow? LastRow { get; set; }
}

/// <summary>
/// Unified shape for parseFinding / resolveMissingField / resolveVocabulary responses — the same
/// three fields cover all three of confirm/missing_field/unknown_value; which are populated
/// depends on ApiEnvelope.Status. Keeping one DTO (rather than one per status) matches the
/// contract's own envelope, which doesn't discriminate the `data` shape by a type tag either.
/// </summary>
public sealed class FindingResultData
{
    /// <summary>Populated on confirm.</summary>
    [JsonPropertyName("pendingRow")]
    public FindingRow? PendingRow { get; set; }

    /// <summary>Populated on missing_field: which field is missing.</summary>
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    /// <summary>Populated on missing_field and unknown_value.</summary>
    [JsonPropertyName("pendingRowSoFar")]
    public FindingRow? PendingRowSoFar { get; set; }

    /// <summary>Populated on unknown_value: which vocabulary category (ROOM/POS/COLOR/SUBSTRATE/COMPONENT).</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Populated on unknown_value: the unrecognized value as spoken/typed.</summary>
    [JsonPropertyName("rawValue")]
    public string? RawValue { get; set; }
}

/// <summary>
/// saveFinding's response isn't a single fixed shape: confirm returns {savedRow}, but the
/// server's defensive re-validation can still bounce back missing_field/unknown_value shapes
/// (same fields as FindingResultData) if the row went stale between resolution and save. One DTO
/// covering both, same pattern as FindingResultData.
/// </summary>
public sealed class SaveFindingData
{
    /// <summary>Populated on confirm.</summary>
    [JsonPropertyName("savedRow")]
    public FindingRow? SavedRow { get; set; }

    /// <summary>Populated on missing_field.</summary>
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    /// <summary>Populated on missing_field and unknown_value.</summary>
    [JsonPropertyName("pendingRowSoFar")]
    public FindingRow? PendingRowSoFar { get; set; }

    /// <summary>Populated on unknown_value.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>Populated on unknown_value.</summary>
    [JsonPropertyName("rawValue")]
    public string? RawValue { get; set; }
}

public sealed class DeleteFindingData
{
    [JsonPropertyName("deletedRow")]
    public FindingRow DeletedRow { get; set; } = new();

    [JsonPropertyName("previousLastRow")]
    public FindingRow? PreviousLastRow { get; set; }
}

public sealed class ReportData
{
    [JsonPropertyName("reportUrl")]
    public string ReportUrl { get; set; } = string.Empty;
}
