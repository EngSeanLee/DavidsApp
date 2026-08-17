namespace DavidsApp.Client.Services;

/// <summary>Thin wrapper over MAUI Essentials' Permissions.</summary>
public sealed class MauiPermissionRequester : IPermissionRequester
{
    public async Task<bool> RequestMicrophoneAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Microphone>();
        }
        return status == PermissionStatus.Granted;
    }
}
