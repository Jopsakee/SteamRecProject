using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SteamRec.Functions.Services;

namespace SteamRec.Functions.Functions;

public class RefreshSteamDataFunction
{
    private readonly ILogger _log;
    private readonly GameRepository _games;
    private readonly SteamAppRepository _apps;
    private readonly SteamStoreClient _steam;
    private readonly IConfiguration _config;

    public RefreshSteamDataFunction(
        ILoggerFactory loggerFactory,
        GameRepository games,
        SteamAppRepository apps,
        SteamStoreClient steam,
        IConfiguration config)
    {
        _log = loggerFactory.CreateLogger<RefreshSteamDataFunction>();
        _games = games;
        _apps = apps;
        _steam = steam;
        _config = config;
    }

    [Function("RefreshSteamData")]
    public async Task Run([TimerTrigger("%SteamRecRefreshSchedule%")] object timerInfo)
    {
        var batchSize = int.TryParse(_config["SteamRec:RefreshBatchSize"], out var n) ? n : 200;
        var cc = _config["SteamRec:CountryCode"] ?? "be";
        var lang = _config["SteamRec:Language"] ?? "en";

        // 2 HTTP calls per app => keep this conservative
        var delayMs = int.TryParse(_config["SteamRec:RequestDelayMs"], out var d) ? d : 1000;

        var staleHours = int.TryParse(_config["SteamRec:StaleHours"], out var sh) ? sh : 24;
        var timeBudgetSeconds = int.TryParse(_config["SteamRec:TimeBudgetSeconds"], out var tb) ? tb : 240;

        var started = DateTime.UtcNow;
        var staleBefore = DateTime.UtcNow.AddHours(-staleHours);

        _log.LogInformation(
            "[RefreshSteamData] Start batchSize={batchSize}, staleHours={staleHours}, delayMs={delayMs}, cc={cc}, lang={lang}, budgetSec={budgetSec}, utcNow={utcNow}",
            batchSize, staleHours, delayMs, cc, lang, timeBudgetSeconds, started);

        var toHydrate = await _apps.GetNextToHydrateAsync(batchSize);

        var remaining = batchSize - toHydrate.Count;
        var stale = remaining > 0
            ? await _games.GetStaleBatchAsync(remaining, staleBefore)
            : new List<GameDocument>();

        _log.LogInformation("[RefreshSteamData] Picked missing={missing} + stale={stale} = total={total}",
            toHydrate.Count, stale.Count, toHydrate.Count + stale.Count);

        int ok = 0, fail = 0;

        foreach (var app in toHydrate)
        {
            if ((DateTime.UtcNow - started).TotalSeconds > timeBudgetSeconds)
            {
                _log.LogWarning("[RefreshSteamData] Time budget reached; stopping early. ok={ok}, fail={fail}", ok, fail);
                break;
            }

            var appId = app.AppId;
            if (appId <= 0) continue;

            try
            {
                var gameDoc = new GameDocument
                {
                    AppId = appId,
                    Name = app.Name ?? ""
                };

                // Track which stage succeeds
                var refreshed = await RefreshOneAsync(gameDoc, cc, lang);
                if (!refreshed)
                {
                    fail++;
                    _log.LogWarning("[RefreshSteamData] Missing->no data fetched appid={appid} (details+reviews both failed)", appId);

                    var currentFails = await _apps.GetFailureCountAsync(appId);
                    await _apps.MarkFailedAsync(appId, currentFails + 1);

                    await Task.Delay(Math.Max(delayMs, 1000));
                    continue;
                }

                _log.LogInformation("[RefreshSteamData] Upserting appid={appid} name={name}", appId, gameDoc.Name);

                await _games.UpsertAsync(gameDoc);
                await _apps.MarkHydratedAsync(appId);

                ok++;
                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                fail++;

                // Force the exception details into the log text (so it can’t “disappear”)
                _log.LogWarning("[RefreshSteamData] Missing->failed appid={appid} exType={type} exMsg={msg} inner={inner}\n{stack}",
                    appId,
                    ex.GetType().FullName,
                    ex.Message,
                    ex.InnerException?.Message,
                    ex.StackTrace);

                // Also try to mark failed, but don't let *that* crash the run
                try
                {
                    var currentFails = await _apps.GetFailureCountAsync(appId);
                    await _apps.MarkFailedAsync(appId, currentFails + 1);
                }
                catch (Exception ex2)
                {
                    _log.LogWarning("[RefreshSteamData] MarkFailed also threw appid={appid} exType={type} exMsg={msg}",
                        appId, ex2.GetType().FullName, ex2.Message);
                }

                await Task.Delay(Math.Max(delayMs, 1000));
            }
        }

        foreach (var g in stale)
        {
            if ((DateTime.UtcNow - started).TotalSeconds > timeBudgetSeconds)
            {
                _log.LogWarning("[RefreshSteamData] Time budget reached; stopping early. ok={ok}, fail={fail}", ok, fail);
                break;
            }

            if (g.AppId <= 0) continue;

            try
            {
                var refreshed = await RefreshOneAsync(g, cc, lang);
                if (!refreshed)
                {
                    fail++;
                    _log.LogWarning("[RefreshSteamData] Stale->no data fetched appid={appid}", g.AppId);
                    await Task.Delay(Math.Max(delayMs, 1000));
                    continue;
                }

                await _games.UpsertAsync(g);

                ok++;
                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                fail++;
                _log.LogWarning("[RefreshSteamData] Stale->failed appid={appid} exType={type} exMsg={msg}\n{stack}",
                    g.AppId, ex.GetType().FullName, ex.Message, ex.StackTrace);
                await Task.Delay(Math.Max(delayMs, 1000));
            }
        }

        _log.LogInformation("[RefreshSteamData] Done ok={ok}, fail={fail}, elapsedSec={sec}",
            ok, fail, (DateTime.UtcNow - started).TotalSeconds);
    }

    private async Task<bool> RefreshOneAsync(GameDocument g, string cc, string lang)
    {
        // If anything throws here, outer catch will show exact type+stack now.
        var details = await _steam.GetAppDetailsAsync(g.AppId, cc, lang);
        var reviews = await _steam.GetReviewSummaryAsync(g.AppId);

        if (!details.ok && !reviews.ok)
            return false;

        if (details.ok)
        {
            if (details.priceEur.HasValue) g.PriceEur = details.priceEur.Value;
            if (details.isFree.HasValue) g.IsFree = details.isFree.Value;
            if (details.requiredAge.HasValue) g.RequiredAge = details.requiredAge.Value;
            if (details.metacritic.HasValue) g.MetacriticScore = details.metacritic.Value;
            if (!string.IsNullOrWhiteSpace(details.genres)) g.Genres = details.genres;
            if (!string.IsNullOrWhiteSpace(details.categories)) g.Categories = details.categories;
        }

        if (reviews.ok)
        {
            g.ReviewPositive = reviews.pos;
            g.ReviewNegative = reviews.neg;
            g.ReviewTotal = reviews.total;
            g.ReviewRatio = reviews.ratio;
            g.ReviewScoreAdj = reviews.scoreAdj;
            g.ReviewVolumeLog = Math.Log10(Math.Max(1, g.ReviewTotal));
        }

        g.UpdatedUtc = DateTime.UtcNow;
        return true;
    }
}
