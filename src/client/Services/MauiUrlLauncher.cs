namespace DavidsApp.Client.Services;

/// <summary>Thin wrapper over MAUI Essentials' Launcher.</summary>
public sealed class MauiUrlLauncher : IUrlLauncher
{
    public Task OpenAsync(string url) => Launcher.Default.OpenAsync(url);
}
