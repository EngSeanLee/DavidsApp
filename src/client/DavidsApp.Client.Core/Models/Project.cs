using System.Text.Json.Serialization;

namespace DavidsApp.Client.Models;

/// <summary>The Projects schema (spec §3). Doubles as startProject's response data shape.</summary>
public sealed class Project
{
    [JsonPropertyName("projectId")]
    public string ProjectId { get; set; } = string.Empty;

    [JsonPropertyName("testingAddress")]
    public string TestingAddress { get; set; } = string.Empty;

    [JsonPropertyName("testingDate")]
    public string? TestingDate { get; set; }

    [JsonPropertyName("jobNumber")]
    public string? JobNumber { get; set; }

    [JsonPropertyName("startTime")]
    public DateTimeOffset? StartTime { get; set; }

    [JsonPropertyName("stopTime")]
    public DateTimeOffset? StopTime { get; set; }

    [JsonPropertyName("createdOn")]
    public DateTimeOffset? CreatedOn { get; set; }
}
