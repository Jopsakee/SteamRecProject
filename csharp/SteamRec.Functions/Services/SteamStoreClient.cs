using System.Net.Http.Json;
using System.Text.Json;

namespace SteamRec.Functions.Services;

public class SteamStoreClient
{
    private readonly HttpClient _http;

    public SteamStoreClient(IHttpClientFactory factory)
    {
        _http = factory.CreateClient();
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<(bool ok, double? priceEur, bool? isFree, int? requiredAge, double? metacritic, string? genres, string? categories)> GetAppDetailsAsync(
        int appId,
        string cc = "be",
        string lang = "en")
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc={cc}&l={lang}";
        using var resp = await _http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return (false, null, null, null, null, null, null);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty(appId.ToString(), out var root)) return (false, null, null, null, null, null, null);
        if (!root.TryGetProperty("success", out var successEl) || !successEl.GetBoolean()) return (false, null, null, null, null, null, null);
        if (!root.TryGetProperty("data", out var data)) return (false, null, null, null, null, null, null);

        bool? isFree = data.TryGetProperty("is_free", out var isFreeEl) ? isFreeEl.GetBoolean() : null;
        int? requiredAge = data.TryGetProperty("required_age", out var ageEl) && ageEl.ValueKind != JsonValueKind.Null ? ageEl.GetInt32() : null;

        double? metacritic = null;
        if (data.TryGetProperty("metacritic", out var metaObj) && metaObj.ValueKind == JsonValueKind.Object)
        {
            if (metaObj.TryGetProperty("score", out var scoreEl) && scoreEl.ValueKind != JsonValueKind.Null)
                metacritic = scoreEl.GetDouble();
        }

        double? priceEur = null;
        if (data.TryGetProperty("price_overview", out var priceObj) && priceObj.ValueKind == JsonValueKind.Object)
        {
            // final is in cents
            if (priceObj.TryGetProperty("final", out var finalEl) && finalEl.ValueKind != JsonValueKind.Null)
                priceEur = finalEl.GetDouble() / 100.0;
        }

        string? genres = null;
        if (data.TryGetProperty("genres", out var genresArr) && genresArr.ValueKind == JsonValueKind.Array)
        {
            var names = genresArr.EnumerateArray()
                .Select(x => x.TryGetProperty("description", out var d) ? d.GetString() : null)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            genres = string.Join(';', names!);
        }

        string? categories = null;
        if (data.TryGetProperty("categories", out var catArr) && catArr.ValueKind == JsonValueKind.Array)
        {
            var names = catArr.EnumerateArray()
                .Select(x => x.TryGetProperty("description", out var d) ? d.GetString() : null)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            categories = string.Join(';', names!);
        }

        return (true, priceEur, isFree, requiredAge, metacritic, genres, categories);
    }

    public async Task<(bool ok, int pos, int neg, int total, double ratio, double scoreAdj)> GetReviewSummaryAsync(int appId)
    {
        var url = $"https://store.steampowered.com/appreviews/{appId}?json=1&language=all&purchase_type=all&num_per_page=0";
        using var resp = await _http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return (false, 0, 0, 0, 0, 0);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        if (json.ValueKind != JsonValueKind.Object) return (false, 0, 0, 0, 0, 0);

        if (!json.TryGetProperty("query_summary", out var qs) || qs.ValueKind != JsonValueKind.Object)
            return (false, 0, 0, 0, 0, 0);

        int pos = qs.TryGetProperty("total_positive", out var p) ? p.GetInt32() : 0;
        int neg = qs.TryGetProperty("total_negative", out var n) ? n.GetInt32() : 0;
        int total = qs.TryGetProperty("total_reviews", out var t) ? t.GetInt32() : (pos + neg);

        double ratio = total > 0 ? (double)pos / total : 0.0;

        // Wilson lower bound (simple “adjusted score”)
        double scoreAdj = WilsonLowerBound(pos, total);

        return (true, pos, neg, total, ratio, scoreAdj);
    }

    private static double WilsonLowerBound(int positive, int total, double z = 1.96)
    {
        if (total <= 0) return 0.0;
        double phat = (double)positive / total;
        double denom = 1 + z * z / total;
        double centre = phat + z * z / (2 * total);
        double margin = z * Math.Sqrt((phat * (1 - phat) + z * z / (4 * total)) / total);
        return (centre - margin) / denom;
    }
}
