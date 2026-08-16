using DavidsApp.Client.Services.Diagnostics;
using DavidsApp.Client.Services.Speech;

namespace DavidsApp.Client.Core.Tests;

/// <summary>Hand-written fakes for CaptureViewModel's other dependencies — same zero-mocking-framework approach as FakeApiClient.</summary>
public sealed class FakeSpeechRecognizer : IContinuousSpeechRecognizer
{
    public bool IsListening { get; private set; }
    public int MuteCallCount { get; private set; }
    public int UnmuteCallCount { get; private set; }
    public bool ThrowOnStart { get; set; }

    public event EventHandler<string>? PartialResult;
    public event EventHandler<string>? FinalResult;
    public event EventHandler<string>? Error;

    public Task StartListeningAsync(CancellationToken ct = default)
    {
        if (ThrowOnStart) throw new InvalidOperationException("Simulated recognizer startup failure.");
        IsListening = true;
        return Task.CompletedTask;
    }

    public Task StopListeningAsync()
    {
        IsListening = false;
        return Task.CompletedTask;
    }

    public void Mute() => MuteCallCount++;
    public void Unmute() => UnmuteCallCount++;

    /// <summary>Test helper — simulates the recognizer producing a final transcript.</summary>
    public void RaiseFinalResult(string transcript) => FinalResult?.Invoke(this, transcript);
    public void RaisePartialResult(string transcript) => PartialResult?.Invoke(this, transcript);
    public void RaiseError(string message) => Error?.Invoke(this, message);
}

public sealed class FakeTextToSpeechService : ITextToSpeechService
{
    public List<string> Spoken { get; } = new();

    public Task SpeakAsync(string text, CancellationToken ct = default)
    {
        Spoken.Add(text);
        return Task.CompletedTask;
    }
}

public sealed class FakeDiagnosticLog : IDiagnosticLog
{
    public List<DiagnosticLogEntry> Entries { get; } = new();

    public Task LogAsync(DiagnosticLogEntry entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}
