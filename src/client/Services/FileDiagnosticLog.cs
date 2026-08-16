using System.Text.Json;
using DavidsApp.Client.Services.Diagnostics;

namespace DavidsApp.Client.Services;

/// <summary>
/// Persists diagnostic entries as newline-delimited JSON in the app's local data directory, for
/// field debugging after a session (build plan step 7). One line per entry, append-only.
/// Serialized via a semaphore rather than relying on FileStream append-atomicity across concurrent
/// callers, since voice input and manual entry could both log in close succession.
/// </summary>
public sealed class FileDiagnosticLog : IDiagnosticLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _filePath;

    public FileDiagnosticLog()
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, "diagnostics.ndjson");
    }

    public async Task LogAsync(DiagnosticLogEntry entry, CancellationToken ct = default)
    {
        var line = JsonSerializer.Serialize(entry, JsonOptions);
        await _writeLock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(_filePath, line + Environment.NewLine, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
