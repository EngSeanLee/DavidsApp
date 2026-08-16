using System.Text.Json;
using System.Text.Json.Serialization;

namespace DavidsApp.Client.Models;

/// <summary>
/// Mirrors the four response statuses defined in docs/api-contract.md. "Unknown" is a client-side
/// fallback for a status string we don't recognize (e.g. a future addition to the contract) —
/// treat it like Error rather than crashing.
/// </summary>
public enum ApiStatus
{
    Confirm,
    MissingField,
    UnknownValue,
    Error,
    Unknown,
}

public sealed class ApiStatusJsonConverter : JsonConverter<ApiStatus>
{
    public override ApiStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() switch
        {
            "confirm" => ApiStatus.Confirm,
            "missing_field" => ApiStatus.MissingField,
            "unknown_value" => ApiStatus.UnknownValue,
            "error" => ApiStatus.Error,
            _ => ApiStatus.Unknown,
        };
    }

    public override void Write(Utf8JsonWriter writer, ApiStatus value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            ApiStatus.Confirm => "confirm",
            ApiStatus.MissingField => "missing_field",
            ApiStatus.UnknownValue => "unknown_value",
            ApiStatus.Error => "error",
            _ => "unknown",
        });
    }
}
