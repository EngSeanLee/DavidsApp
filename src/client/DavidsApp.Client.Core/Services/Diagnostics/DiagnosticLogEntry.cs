namespace DavidsApp.Client.Services.Diagnostics;

/// <summary>
/// One structured diagnostic log entry — build plan step 7: "timestamp, projectId, raw STT text,
/// API action/status, error category, pending row, last-saved-row ID." Deliberately a flat record
/// covering every event kind (raw_stt / voice_command / api_call / state_change) rather than a
/// type per kind, so a field debugging session can just scan one file/table in Timestamp order.
/// </summary>
public sealed record DiagnosticLogEntry(
    DateTimeOffset Timestamp,
    string EventType,
    string? ProjectId = null,
    /// <summary>Raw recognized text, logged separately from whatever the parser made of it — spec
    /// §5.3: isolates STT errors from parsing errors when debugging a field report.</summary>
    string? RawTranscript = null,
    string? Action = null,
    string? Status = null,
    string? ErrorCode = null,
    string? PendingRowSummary = null,
    string? LastSavedRowSummary = null,
    string? Message = null);

/// <summary>Sink for diagnostic entries. The MAUI app persists these to a local file for field
/// debugging (see FileDiagnosticLog); tests use an in-memory fake.</summary>
public interface IDiagnosticLog
{
    Task LogAsync(DiagnosticLogEntry entry, CancellationToken ct = default);
}
