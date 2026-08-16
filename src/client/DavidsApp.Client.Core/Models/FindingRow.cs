using System.Text.Json.Serialization;

namespace DavidsApp.Client.Models;

/// <summary>
/// The Findings schema (spec §3), used for pendingRow / pendingRowSoFar / savedRow / lastRow /
/// deletedRow — all the same shape across the contract, just at different stages of completeness
/// (hence everything nullable except the fields that are always required once a row exists).
/// </summary>
public sealed class FindingRow
{
    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }

    /// <summary>The Findings sheet's "#" column — only present once a row has been saved.</summary>
    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("room")]
    public string? Room { get; set; }

    [JsonPropertyName("wall")]
    public string? Wall { get; set; }

    [JsonPropertyName("pos")]
    public string? Pos { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("substrate")]
    public string? Substrate { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("component")]
    public string? Component { get; set; }

    [JsonPropertyName("reading")]
    public string? Reading { get; set; }

    [JsonPropertyName("enteredOn")]
    public DateTimeOffset? EnteredOn { get; set; }

    /// <summary>True once every field required to save is populated (mirrors Findings.js's REQUIRED_FINDING_FIELDS_).</summary>
    public bool IsComplete =>
        !string.IsNullOrEmpty(Room) && !string.IsNullOrEmpty(Wall) && !string.IsNullOrEmpty(Pos) &&
        !string.IsNullOrEmpty(Color) && !string.IsNullOrEmpty(Substrate) && !string.IsNullOrEmpty(State) &&
        !string.IsNullOrEmpty(Component) && !string.IsNullOrEmpty(Reading);
}
