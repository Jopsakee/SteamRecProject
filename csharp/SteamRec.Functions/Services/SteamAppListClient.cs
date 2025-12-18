using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SteamRec.Functions.Services;

public class SteamAppListClient
{
    private readonly HttpClient _http;

    public SteamAppListClient(IHttpClientFactory factory)
    {
        _http = factory.CreateClient();
        _http.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<List<(int appId, string name)>> GetAppListAsync()
    {
        // No API key needed
        var url = "https://api.steampowered.com/ISteamApps/GetAppList/v2/";

        var dto = await _http.GetFromJsonAsync<AppListResponse>(url);
        var apps = dto?.applist?.apps ?? new List<AppDto>();

        return apps
            .Where(a => a.appid > 0)
            .Select(a => (a.appid, a.name ?? ""))
            .ToList();
    }

    private class AppListResponse
    {
        public AppList? applist { get; set; }
    }

    private class AppList
    {
        public List<AppDto> apps { get; set; } = new();
    }

    private class AppDto
    {
        public int appid { get; set; }
        public string? name { get; set; }
    }
}
