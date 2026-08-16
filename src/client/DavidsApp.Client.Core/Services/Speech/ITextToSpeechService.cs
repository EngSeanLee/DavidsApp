namespace DavidsApp.Client.Services.Speech;

/// <summary>
/// Abstracts MAUI Essentials' TextToSpeech behind an interface so ViewModels in Core can use it
/// without a MAUI dependency. Implementation coordinates with IContinuousSpeechRecognizer per
/// spec §5.3: the mic must never listen while the app is speaking — callers are responsible for
/// muting the recognizer before SpeakAsync and unmuting after (see CaptureViewModel).
/// </summary>
public interface ITextToSpeechService
{
    Task SpeakAsync(string text, CancellationToken ct = default);
}
