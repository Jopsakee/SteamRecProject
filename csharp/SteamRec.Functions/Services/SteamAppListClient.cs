using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace SteamRec.Functions.Services;

public class SteamAppListClient
{
    private const string BaseUrl = "https://api.steampowered.com/IStoreService/GetAppList/v1/";

    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public SteamAppListClient(IHttpClientFactory factory, IConfiguration config)
    {
        _http = factory.CreateClient();
        _http.Timeout = TimeSpan.FromSeconds(60);

        // Helps avoid occasional blocking
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SteamRecProject/1.0 (+AzureFunctions)");

        _config = config;
    }

    public async Task<(List<(int appId, string name)> apps, bool haveMore, int lastAppId)> GetAppListPageAsync(
        int lastAppId,
        int maxResults = 50000,
        bool includeGames = true,
        bool includeDlc = false,
        bool includeSoftware = false,
        bool includeVideos = false,
        bool includeHardware = false,
        uint? ifModifiedSince = null,
        CancellationToken ct = default)
    {
        var key =
            _config["Steam:ApiKey"] ??
            Environment.GetEnvironmentVariable("STEAM_API_KEY");

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Steam API key missing. Set Steam__ApiKey (Azure) or STEAM_API_KEY (local).");

        // IStoreService/GetAppList supports these query params. 
        var url =
            $"{BaseUrl}?key={Uri.EscapeDataString(key)}" +
            $"&max_results={maxResults}" +
            $"&last_appid={lastAppId}" +
            $"&include_games={(includeGames ? "true" : "false")}" +
            $"&include_dlc={(includeDlc ? "true" : "false")}" +
            $"&include_software={(includeSoftware ? "true" : "false")}" +
            $"&include_videos={(includeVideos ? "true" : "false")}" +
            $"&include_hardware={(includeHardware ? "true" : "false")}";

        if (ifModifiedSince.HasValue)
            url += $"&if_modified_since={ifModifiedSince.Value}";

        using var resp = await _http.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            var snippet = body.Length > 600 ? body[..600] + "..." : body;
            throw new HttpRequestException($"Steam GetAppList failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {snippet}");
        }

        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("response", out var response))
            return (new List<(int, string)>(), false, lastAppId);

        var apps = new List<(int appId, string name)>();

        if (response.TryGetProperty("apps", out var appsArr) && appsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in appsArr.EnumerateArray())
            {
                if (!a.TryGetProperty("appid", out var idEl)) continue;
                var id = idEl.GetInt32();
                if (id <= 0) continue;

                var name = a.TryGetProperty("name", out var nmEl) ? (nmEl.GetString() ?? "") : "";
                apps.Add((id, name));
            }
        }

        var haveMore =
            response.TryGetProperty("have_more_results", out var hmEl) &&
            hmEl.ValueKind != JsonValueKind.Null &&
            hmEl.GetBoolean();

        // Some responses include last_appid; fallback to last returned app in the page
        var newLast = lastAppId;

        if (response.TryGetProperty("last_appid", out var lastEl) && lastEl.ValueKind != JsonValueKind.Null)
            newLast = lastEl.GetInt32();
        else if (apps.Count > 0)
            newLast = apps[^1].appId;

        return (apps, haveMore, newLast);
    }
}
