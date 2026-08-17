namespace DavidsApp.Client.Services;

/// <summary>
/// Abstracts MAUI Essentials' Permissions behind an interface so CaptureViewModel can request the
/// microphone at startup without a MAUI dependency — same pattern as ITextToSpeechService/
/// IUrlLauncher. Android treats RECORD_AUDIO as a "dangerous" runtime permission: declaring it in
/// AndroidManifest.xml is necessary but not sufficient, the app must also prompt for it at
/// runtime, or the speech recognizer silently has no audio access. This was a real gap found
/// before first real-device testing — nothing previously called this anywhere.
/// </summary>
public interface IPermissionRequester
{
    /// <summary>Prompts for the microphone permission if not already granted. Returns whether it's granted after the prompt.</summary>
    Task<bool> RequestMicrophoneAsync();
}
