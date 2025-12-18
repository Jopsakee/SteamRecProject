using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SteamRec.Functions.Services;

public class SteamStoreClient
{
    private readonly HttpClient _http;
    private readonly ILogger<SteamStoreClient> _log;

    public SteamStoreClient(IHttpClientFactory factory, ILogger<SteamStoreClient> log)
    {
        _log = log;

        // IMPORTANT: use the named client configured in Program.cs
        _http = factory.CreateClient("steam");
        _http.Timeout = TimeSpan.FromSeconds(30);

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SteamRecProject/1.0 (+AzureFunctions)");
    }

    public async Task<(bool ok, double? priceEur, bool? isFree, int? requiredAge, double? metacritic, string? genres, string? categories)>
        GetAppDetailsAsync(int appId, string cc = "be", string lang = "en", CancellationToken ct = default)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&cc={cc}&l={lang}";

        var (okHttp, body, status, contentType) = await GetStringWithRetryAsync(url, ct);
        if (!okHttp || body is null) return (false, null, null, null, null, null, null);

        if (!LooksLikeJson(contentType, body))
        {
            _log.LogWarning("[SteamStoreClient] appdetails got non-JSON response appid={appid} status={status} contentType={ct}. BodySnippet={snippet}",
                appId, (int)status, contentType ?? "(none)", Snip(body));
            return (false, null, null, null, null, null, null);
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[SteamStoreClient] appdetails JSON parse failed appid={appid} status={status} ct={ct}. BodySnippet={snippet}",
                appId, (int)status, contentType ?? "(none)", Snip(body));
            return (false, null, null, null, null, null, null);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty(appId.ToString(), out var root)) return (false, null, null, null, null, null, null);
            if (!root.TryGetProperty("success", out var successEl) || successEl.ValueKind != JsonValueKind.True) return (false, null, null, null, null, null, null);
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return (false, null, null, null, null, null, null);

            bool? isFree = null;
            if (data.TryGetProperty("is_free", out var isFreeEl) &&
                (isFreeEl.ValueKind == JsonValueKind.True || isFreeEl.ValueKind == JsonValueKind.False))
                isFree = isFreeEl.GetBoolean();

            int? requiredAge = null;
            if (data.TryGetProperty("required_age", out var ageEl) && ageEl.ValueKind != JsonValueKind.Null)
            {
                if (ageEl.ValueKind == JsonValueKind.Number && ageEl.TryGetInt32(out var ageNum))
                    requiredAge = ageNum;
                else if (ageEl.ValueKind == JsonValueKind.String && int.TryParse(ageEl.GetString(), out var ageStr))
                    requiredAge = ageStr;
            }

            double? metacritic = null;
            if (data.TryGetProperty("metacritic", out var metaObj) && metaObj.ValueKind == JsonValueKind.Object)
            {
                if (metaObj.TryGetProperty("score", out var scoreEl) && scoreEl.ValueKind != JsonValueKind.Null)
                {
                    if (scoreEl.ValueKind == JsonValueKind.Number) metacritic = scoreEl.GetDouble();
                    else if (scoreEl.ValueKind == JsonValueKind.String && double.TryParse(scoreEl.GetString(), out var s)) metacritic = s;
                }
            }

            double? priceEur = null;
            if (data.TryGetProperty("price_overview", out var priceObj) && priceObj.ValueKind == JsonValueKind.Object)
            {
                if (priceObj.TryGetProperty("final", out var finalEl) && finalEl.ValueKind == JsonValueKind.Number)
                    priceEur = finalEl.GetDouble() / 100.0;
            }

            string? genres = null;
            if (data.TryGetProperty("genres", out var genresArr) && genresArr.ValueKind == JsonValueKind.Array)
            {
                var names = genresArr.EnumerateArray()
                    .Select(x => x.TryGetProperty("description", out var d) ? d.GetString() : null)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();
                if (names.Length > 0) genres = string.Join(';', names!);
            }

            string? categories = null;
            if (data.TryGetProperty("categories", out var catArr) && catArr.ValueKind == JsonValueKind.Array)
            {
                var names = catArr.EnumerateArray()
                    .Select(x => x.TryGetProperty("description", out var d) ? d.GetString() : null)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();
                if (names.Length > 0) categories = string.Join(';', names!);
            }

            return (true, priceEur, isFree, requiredAge, metacritic, genres, categories);
        }
    }

    public async Task<(bool ok, int pos, int neg, int total, double ratio, double scoreAdj)>
        GetReviewSummaryAsync(int appId, CancellationToken ct = default)
    {
        var url = $"https://store.steampowered.com/appreviews/{appId}?json=1&language=all&purchase_type=all&num_per_page=0";

        var (okHttp, body, status, contentType) = await GetStringWithRetryAsync(url, ct);
        if (!okHttp || body is null) return (false, 0, 0, 0, 0, 0);

        if (!LooksLikeJson(contentType, body))
        {
            _log.LogWarning("[SteamStoreClient] reviews got non-JSON response appid={appid} status={status} contentType={ct}. BodySnippet={snippet}",
                appId, (int)status, contentType ?? "(none)", Snip(body));
            return (false, 0, 0, 0, 0, 0);
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[SteamStoreClient] reviews JSON parse failed appid={appid} status={status} ct={ct}. BodySnippet={snippet}",
                appId, (int)status, contentType ?? "(none)", Snip(body));
            return (false, 0, 0, 0, 0, 0);
        }

        using (doc)
        {
            var json = doc.RootElement;
            if (json.ValueKind != JsonValueKind.Object) return (false, 0, 0, 0, 0, 0);

            if (!json.TryGetProperty("query_summary", out var qs) || qs.ValueKind != JsonValueKind.Object)
                return (false, 0, 0, 0, 0, 0);

            int pos = qs.TryGetProperty("total_positive", out var p) ? p.GetInt32() : 0;
            int neg = qs.TryGetProperty("total_negative", out var n) ? n.GetInt32() : 0;
            int total = qs.TryGetProperty("total_reviews", out var t) ? t.GetInt32() : (pos + neg);

            double ratio = total > 0 ? (double)pos / total : 0.0;
            double scoreAdj = WilsonLowerBound(pos, total);

            return (true, pos, neg, total, ratio, scoreAdj);
        }
    }

    private async Task<(bool ok, string? body, HttpStatusCode status, string? contentType)> GetStringWithRetryAsync(string url, CancellationToken ct)
    {
        const int maxRetries = 3;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            using var resp = await _http.GetAsync(url, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var ctHeader = resp.Content.Headers.ContentType?.ToString();

            if (resp.IsSuccessStatusCode)
                return (true, body, resp.StatusCode, ctHeader);

            if (resp.StatusCode == (HttpStatusCode)429 || resp.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                var wait = GetRetryAfter(resp) ?? TimeSpan.FromSeconds(Math.Min(30, 2 + Math.Pow(2, attempt)));
                _log.LogWarning("[SteamStoreClient] throttled status={status}. attempt={attempt}/{max}. waiting={waitSec}s",
                    (int)resp.StatusCode, attempt + 1, maxRetries + 1, wait.TotalSeconds);

                if (attempt == maxRetries)
                    return (false, body, resp.StatusCode, ctHeader);

                await Task.Delay(wait, ct);
                continue;
            }

            _log.LogWarning("[SteamStoreClient] non-200 status={status} ct={ct}. BodySnippet={snippet}",
                (int)resp.StatusCode, ctHeader ?? "(none)", Snip(body));

            return (false, body, resp.StatusCode, ctHeader);
        }

        return (false, null, 0, null);
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage resp)
    {
        var ra = resp.Headers.RetryAfter;
        if (ra?.Delta != null) return ra.Delta;
        if (ra?.Date != null)
        {
            var delta = ra.Date.Value - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : TimeSpan.FromSeconds(5);
        }
        return null;
    }

    private static bool LooksLikeJson(string? contentType, string body)
    {
        if (!string.IsNullOrWhiteSpace(contentType) &&
            contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return true;

        var s = body.TrimStart();
        return s.StartsWith("{") || s.StartsWith("[");
    }

    private static string Snip(string s)
        => string.IsNullOrEmpty(s) ? "" : (s.Length > 300 ? s[..300] + "..." : s);

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
