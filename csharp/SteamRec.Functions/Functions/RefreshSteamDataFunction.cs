using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SteamRec.Functions.Services;

namespace SteamRec.Functions.Functions;

public class RefreshSteamDataFunction
{
    private readonly ILogger _log;
    private readonly GameRepository _repo;
    private readonly SteamStoreClient _steam;
    private readonly IConfiguration _config;

    public RefreshSteamDataFunction(
        ILoggerFactory loggerFactory,
        GameRepository repo,
        SteamStoreClient steam,
        IConfiguration config)
    {
        _log = loggerFactory.CreateLogger<RefreshSteamDataFunction>();
        _repo = repo;
        _steam = steam;
        _config = config;
    }

    [Function("RefreshSteamData")]
    public async Task Run([TimerTrigger("%SteamRecRefreshSchedule%")] object timerInfo)
    {
        var batchSize = int.TryParse(_config["SteamRec:RefreshBatchSize"], out var n) ? n : 200;
        var cc = _config["SteamRec:CountryCode"] ?? "be";
        var lang = _config["SteamRec:Language"] ?? "en";

        _log.LogInformation("[RefreshSteamData] Starting refresh. batchSize={batchSize}, cc={cc}, lang={lang}, utcNow={utcNow}",
            batchSize, cc, lang, DateTime.UtcNow);

        var batch = await _repo.GetBatchToRefreshAsync(batchSize);
        _log.LogInformation("[RefreshSteamData] Picked {count} games to refresh.", batch.Count);

        int ok = 0, fail = 0;

        foreach (var g in batch)
        {
            try
            {
                // 1) appdetails (price/flags/genres/categories)
                var details = await _steam.GetAppDetailsAsync(g.AppId, cc, lang);

                if (details.ok)
                {
                    if (details.priceEur.HasValue) g.PriceEur = details.priceEur.Value;
                    if (details.isFree.HasValue) g.IsFree = details.isFree.Value;
                    if (details.requiredAge.HasValue) g.RequiredAge = details.requiredAge.Value;
                    if (details.metacritic.HasValue) g.MetacriticScore = details.metacritic.Value;
                    if (!string.IsNullOrWhiteSpace(details.genres)) g.Genres = details.genres;
                    if (!string.IsNullOrWhiteSpace(details.categories)) g.Categories = details.categories;
                }

                // 2) reviews summary
                var reviews = await _steam.GetReviewSummaryAsync(g.AppId);
                if (reviews.ok)
                {
                    g.ReviewPositive = reviews.pos;
                    g.ReviewNegative = reviews.neg;
                    g.ReviewTotal = reviews.total;
                    g.ReviewRatio = reviews.ratio;
                    g.ReviewScoreAdj = reviews.scoreAdj;
                }

                g.UpdatedUtc = DateTime.UtcNow;

                await _repo.UpsertAsync(g);
                ok++;

                // gentle pacing (avoid hammering Steam)
                await Task.Delay(150);
            }
            catch (Exception ex)
            {
                fail++;
                _log.LogWarning(ex, "[RefreshSteamData] Failed for appid={appid}", g.AppId);
                await Task.Delay(300);
            }
        }

        _log.LogInformation("[RefreshSteamData] Done. ok={ok}, fail={fail}", ok, fail);
    }
}
