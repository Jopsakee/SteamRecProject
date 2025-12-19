using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SteamRec.Functions.Services;

public class SteamSpyClient
{
    private readonly HttpClient _http;
    private readonly ILogger<SteamSpyClient> _log;

    public SteamSpyClient(IHttpClientFactory factory, ILogger<SteamSpyClient> log)
    {
        _log = log;
        _http = factory.CreateClient("steamspy");
        _http.Timeout = TimeSpan.FromSeconds(30);

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SteamRecProject/1.0 (+AzureFunctions)");
    }

    /// Returns semicolon-delimited top tags (by count), or null if unavailable.
    public async Task<(bool ok, string? tags)> GetTopTagsAsync(int appId, int maxTags = 15, CancellationToken ct = default)
    {
        if (appId <= 0) return (false, null);

        var url = $"https://steamspy.com/api.php?request=appdetails&appid={appId}";

        var (okHttp, body, status, contentType) = await GetStringWithRetryAsync(url, ct);
        if (!okHttp || body is null) return (false, null);

        if (!LooksLikeJson(contentType, body))
        {
            _log.LogWarning("[SteamSpyClient] non-JSON response appid={appid} status={status} ct={ct}. BodySnippet={snippet}",
                appId, (int)status, contentType ?? "(none)", Snip(body));
            return (false, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (false, null);

            if (!root.TryGetProperty("tags", out var tagsObj) || tagsObj.ValueKind != JsonValueKind.Object)
                return (true, null);

            var pairs = new List<(string name, int count)>();
            foreach (var prop in tagsObj.EnumerateObject())
            {
                int count = 0;
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var n)) count = n;
                else if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out var ns)) count = ns;

                if (!string.IsNullOrWhiteSpace(prop.Name) && count > 0)
                    pairs.Add((prop.Name, count));
            }

            if (pairs.Count == 0) return (true, null);

            var top = pairs
                .OrderByDescending(x => x.count)
                .Take(Math.Max(1, maxTags))
                .Select(x => x.name.Trim())
                .Where(x => x.Length > 0)
                .ToArray();

            return (true, top.Length > 0 ? string.Join(';', top) : null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[SteamSpyClient] JSON parse failed appid={appid} status={status} ct={ct}. BodySnippet={snippet}",
                appId, (int)status, contentType ?? "(none)", Snip(body));
            return (false, null);
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
                _log.LogWarning("[SteamSpyClient] throttled status={status}. attempt={attempt}/{max}. waiting={waitSec}s",
                    (int)resp.StatusCode, attempt + 1, maxRetries + 1, wait.TotalSeconds);

                if (attempt == maxRetries)
                    return (false, body, resp.StatusCode, ctHeader);

                await Task.Delay(wait, ct);
                continue;
            }

            _log.LogWarning("[SteamSpyClient] non-200 status={status} ct={ct}. BodySnippet={snippet}",
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
}
