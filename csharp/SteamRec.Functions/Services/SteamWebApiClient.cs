using System.Net.Http.Json;
using System.Text.Json;

namespace SteamRec.Functions.Services;

public class SteamWebApiClient
{
    private readonly HttpClient _http;

    public SteamWebApiClient(IHttpClientFactory factory)
    {
        _http = factory.CreateClient();
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<List<(int appId, string name)>> GetAppListAsync()
    {
        var url = "https://api.steampowered.com/ISteamApps/GetAppList/v2/";
        var json = await _http.GetFromJsonAsync<JsonElement>(url);

        if (json.ValueKind != JsonValueKind.Object) return new();

        if (!json.TryGetProperty("applist", out var appListObj)) return new();
        if (!appListObj.TryGetProperty("apps", out var appsArr)) return new();
        if (appsArr.ValueKind != JsonValueKind.Array) return new();

        var result = new List<(int, string)>();

        foreach (var a in appsArr.EnumerateArray())
        {
            int appid = a.TryGetProperty("appid", out var idEl) ? idEl.GetInt32() : 0;
            string name = a.TryGetProperty("name", out var nmEl) ? (nmEl.GetString() ?? "") : "";
            if (appid > 0) result.Add((appid, name));
        }

        return result;
    }
}
