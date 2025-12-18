using System.Text.Json;

namespace SteamRec.Functions.Services;

public class SteamAppListClient
{
    private readonly HttpClient _http;

    public SteamAppListClient(IHttpClientFactory factory)
    {
        _http = factory.CreateClient();
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<List<(int appId, string name)>> GetAllAppsAsync()
    {
        // Official Steam endpoint for the full app list
        var url = "https://api.steampowered.com/ISteamApps/GetAppList/v2/";

        using var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var apps = doc.RootElement
            .GetProperty("applist")
            .GetProperty("apps")
            .EnumerateArray()
            .Select(a =>
            {
                var appId = a.TryGetProperty("appid", out var idEl) ? idEl.GetInt32() : 0;
                var name = a.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? "") : "";
                return (appId, name);
            })
            .Where(x => x.appId > 0)
            .ToList();

        return apps;
    }
}
