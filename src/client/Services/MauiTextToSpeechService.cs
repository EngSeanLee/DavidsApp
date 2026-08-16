using DavidsApp.Client.Services.Speech;

namespace DavidsApp.Client.Services;

/// <summary>Thin wrapper over MAUI Essentials' TextToSpeech so Core's ViewModels can depend on the interface, not MAUI.</summary>
public sealed class MauiTextToSpeechService : ITextToSpeechService
{
    public Task SpeakAsync(string text, CancellationToken ct = default) =>
        TextToSpeech.Default.SpeakAsync(text, cancelToken: ct);
}
