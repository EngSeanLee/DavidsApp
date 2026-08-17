using System.Text.Json;
using DavidsApp.Client.Services.Api;

namespace DavidsApp.Client.Services;

/// <summary>
/// Optionally overrides ApiClientOptions from Resources/Raw/appsettings.local.json, bundled as a
/// MauiAsset if present. That file is gitignored (**/appsettings.local.json) — it never reaches
/// source control, so the real deployment URL/shared secret (a bearer credential — see
/// docs/decisions/0002-auth-and-secrets.md) never do either. A fresh checkout with no local config
/// silently falls back to the mock-API defaults already on ApiClientOptions.
///
/// Every await here uses ConfigureAwait(false) deliberately: MauiProgram calls this via
/// GetAwaiter().GetResult() (a synchronous block) during CreateMauiApp(), before the platform's
/// UI/run loop is pumping normally. On iOS specifically, FileSystem.OpenAppPackageFileAsync's
/// continuation otherwise wants to resume on the captured main-thread SynchronizationContext —
/// but that thread is the one synchronously blocked waiting for this method, so it deadlocks.
/// Symptom was the splash screen freezing for ~10s before iOS's launch watchdog killed the app —
/// not a network issue at all, despite looking like one from the outside. ConfigureAwait(false)
/// lets continuations run on a thread-pool thread instead, breaking the deadlock.
/// </summary>
public static class LocalConfigLoader
{
    private const string FileName = "appsettings.local.json";

    public static async Task<ApiClientOptions> LoadAsync()
    {
        var options = new ApiClientOptions();
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(FileName).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<ApiClientOptions>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (loaded is not null)
            {
                options = loaded;
            }
        }
        catch (FileNotFoundException)
        {
            // Expected on a fresh checkout — no local override present, keep mock-API defaults.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LocalConfigLoader: failed to load {FileName}, using defaults. {ex}");
        }
        return options;
    }
}
