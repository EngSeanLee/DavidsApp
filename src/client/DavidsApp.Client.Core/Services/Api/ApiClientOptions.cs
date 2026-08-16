namespace DavidsApp.Client.Services.Api;

/// <summary>
/// Points the client at either tools/mock-api (dev default) or the real deployed Apps Script Web
/// App. Real values (a live deployment URL + shared secret) belong in a gitignored local config —
/// never hardcoded here and never committed. See backend/apps-script/README.md for where to find
/// them once Phase 1's deployment exists.
/// </summary>
public sealed class ApiClientOptions
{
    /// <summary>
    /// Defaults to the local mock API (tools/mock-api) so a fresh checkout runs against
    /// something out of the box. Port 4127 rather than a common default like 3000/8080 —
    /// deliberately picked to avoid colliding with other local dev servers.
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:4127/";

    /// <summary>The mock API doesn't enforce this for real; any non-empty value works against it.</summary>
    public string ApiKey { get; set; } = "dev-mock-key";
}
