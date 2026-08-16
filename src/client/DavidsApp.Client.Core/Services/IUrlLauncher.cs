namespace DavidsApp.Client.Services;

/// <summary>Abstracts MAUI's Launcher (opens a URL in the system browser/viewer) behind an
/// interface so CaptureViewModel can stay MAUI-free, same pattern as ITextToSpeechService.</summary>
public interface IUrlLauncher
{
    Task OpenAsync(string url);
}
