using System.Text.Json.Serialization;

namespace DavidsApp.Client.Models;

/// <summary>
/// The response envelope every action returns. See docs/api-contract.md "Response envelope".
/// TData is the action-specific shape of `data` — see the *Data classes in this folder.
/// </summary>
public sealed class ApiEnvelope<TData>
{
    [JsonConverter(typeof(ApiStatusJsonConverter))]
    [JsonPropertyName("status")]
    public ApiStatus Status { get; init; } = ApiStatus.Unknown;

    [JsonPropertyName("data")]
    public TData? Data { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }
}

/// <summary>The request envelope every action sends. See docs/api-contract.md "Request envelope".</summary>
public sealed class ApiRequest<TPayload>
{
    [JsonPropertyName("apiKey")]
    public required string ApiKey { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("payload")]
    public required TPayload Payload { get; init; }
}
