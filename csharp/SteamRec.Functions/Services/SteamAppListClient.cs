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

        // Some endpoints are picky; a UA helps avoid random blocking.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SteamRecProject/1.0 (+AzureFunctions)");
    }

    public async Task<List<(int appId, string name)>> GetAppListAsync(CancellationToken ct = default)
    {
        var url = "https://api.steampowered.com/ISteamApps/GetAppList/v2/";

        using var resp = await _http.GetAsync(url, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Steam GetAppList failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");
        }

        var root = await resp.Content.ReadFromJsonAsync<AppListRoot>(cancellationToken: ct);
        var apps = root?.applist?.apps ?? new List<AppItem>();

        return apps
            .Where(a => a.appid > 0)
            .Select(a => (a.appid, a.name ?? ""))
            .ToList();
    }

    public class AppListRoot
    {
        public AppList? applist { get; set; }
    }

    public class AppList
    {
        public List<AppItem>? apps { get; set; }
    }

    public class AppItem
    {
        public int appid { get; set; }
        public string? name { get; set; }
    }
}
