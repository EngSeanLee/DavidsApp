using System.Text.Json;
using DavidsApp.Client.Services.Api;

namespace DavidsApp.Client.Services;

/// <summary>
/// Optionally overrides ApiClientOptions from Resources/Raw/appsettings.local.json, bundled as a
/// MauiAsset if present. That file is gitignored (**/appsettings.local.json) — it never reaches
/// source control, so the real deployment URL/shared secret (a bearer credential — see
/// docs/decisions/0002-auth-and-secrets.md) never do either. A fresh checkout with no local config
/// silently falls back to the mock-API defaults already on ApiClientOptions.
/// </summary>
public static class LocalConfigLoader
{
    private const string FileName = "appsettings.local.json";

    public static async Task<ApiClientOptions> LoadAsync()
    {
        var options = new ApiClientOptions();
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(FileName);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
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
