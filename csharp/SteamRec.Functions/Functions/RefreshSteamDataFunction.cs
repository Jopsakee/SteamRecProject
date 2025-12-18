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

        var delayMs = int.TryParse(_config["SteamRec:RequestDelayMs"], out var d) ? d : 200;

        // How stale before we refresh existing docs (hours)
        var staleHours = int.TryParse(_config["SteamRec:StaleHours"], out var sh) ? sh : 24;

        // Optional time budget so you never risk “too long” runs
        var timeBudgetSeconds = int.TryParse(_config["SteamRec:TimeBudgetSeconds"], out var tb) ? tb : 240;

        var started = DateTime.UtcNow;
        var staleBefore = DateTime.UtcNow.AddHours(-staleHours);

        _log.LogInformation(
            "[RefreshSteamData] Start batchSize={batchSize}, staleHours={staleHours}, delayMs={delayMs}, cc={cc}, lang={lang}, budgetSec={budgetSec}, utcNow={utcNow}",
            batchSize, staleHours, delayMs, cc, lang, timeBudgetSeconds, started);

        // 1) Prefer missing games (bootstrap mode)
        var toHydrate = await _apps.GetNextToHydrateAsync(batchSize);

        // 2) If not enough missing, fill remainder with stale games (maintenance mode)
        var remaining = batchSize - toHydrate.Count;
        var stale = remaining > 0
            ? await _games.GetStaleBatchAsync(remaining, staleBefore)
            : new List<GameDocument>();

        _log.LogInformation("[RefreshSteamData] Picked missing={missing} + stale={stale} = total={total}",
            toHydrate.Count, stale.Count, toHydrate.Count + stale.Count);

        int ok = 0, fail = 0, skipped = 0;

        // --- Process missing first ---
        foreach (var app in toHydrate)
        {
            if ((DateTime.UtcNow - started).TotalSeconds > timeBudgetSeconds)
            {
                _log.LogWarning("[RefreshSteamData] Time budget reached; stopping early. ok={ok}, fail={fail}, skipped={skipped}", ok, fail, skipped);
                break;
            }

            var appId = app.AppId;
            if (appId <= 0) continue;

            try
            {
                // Build doc first, but only upsert if it's truly a "game"
                var gameDoc = new GameDocument
                {
                    AppId = appId,
                    Name = app.Name ?? "",
                    UpdatedUtc = DateTime.UtcNow
                };

                var shouldUpsert = await RefreshOneAsync(gameDoc, cc, lang);

                if (!shouldUpsert)
                {
                    skipped++;
                    _log.LogInformation("[RefreshSteamData] Skipped non-game appid={appid} name={name}", appId, gameDoc.Name);

                    // Important: mark hydrated so we don't keep retrying forever
                    await _apps.MarkHydratedAsync(appId);
                    await Task.Delay(delayMs);
                    continue;
                }

                _log.LogInformation("[RefreshSteamData] Upserting appid={appid} name={name}", gameDoc.AppId, gameDoc.Name);

                await _games.UpsertAsync(gameDoc);
                await _apps.MarkHydratedAsync(appId);

                ok++;
                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                fail++;
                _log.LogWarning(ex, "[RefreshSteamData] Missing->failed appid={appid}", appId);

                var currentFails = await _apps.GetFailureCountAsync(appId);
                await _apps.MarkFailedAsync(appId, currentFails + 1);

                await Task.Delay(Math.Max(delayMs, 300));
            }
        }

        // --- Then process stale games ---
        foreach (var g in stale)
        {
            if ((DateTime.UtcNow - started).TotalSeconds > timeBudgetSeconds)
            {
                _log.LogWarning("[RefreshSteamData] Time budget reached; stopping early. ok={ok}, fail={fail}, skipped={skipped}", ok, fail, skipped);
                break;
            }

            if (g.AppId <= 0) continue;

            try
            {
                var shouldUpsert = await RefreshOneAsync(g, cc, lang);
                if (!shouldUpsert)
                {
                    skipped++;
                    _log.LogInformation("[RefreshSteamData] Skipped non-game (stale list) appid={appid} name={name}", g.AppId, g.Name);
                    await Task.Delay(delayMs);
                    continue;
                }

                await _games.UpsertAsync(g);

                ok++;
                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                fail++;
                _log.LogWarning(ex, "[RefreshSteamData] Stale->failed appid={appid}", g.AppId);
                await Task.Delay(Math.Max(delayMs, 300));
            }
        }

        _log.LogInformation("[RefreshSteamData] Done ok={ok}, fail={fail}, skipped={skipped}, elapsedSec={sec}",
            ok, fail, skipped, (DateTime.UtcNow - started).TotalSeconds);
    }

    /// <summary>
    /// Returns true if this app should be upserted into games (i.e., it is a real "game").
    /// </summary>
    private async Task<bool> RefreshOneAsync(GameDocument g, string cc, string lang)
    {
        // Store details (price, tags etc.)
        var details = await _steam.GetAppDetailsAsync(g.AppId, cc, lang);

        if (!details.ok)
        {
            // If appdetails fails, we still let reviews try; but we can't decide type reliably.
            // We'll treat as "not upsertable" so we don't insert junk docs.
            return false;
        }

        // Filter to only "game"
        if (!string.Equals(details.type, "game", StringComparison.OrdinalIgnoreCase))
            return false;

        if (details.priceEur.HasValue) g.PriceEur = details.priceEur.Value;
        if (details.isFree.HasValue) g.IsFree = details.isFree.Value;
        if (details.requiredAge.HasValue) g.RequiredAge = details.requiredAge.Value;
        if (details.metacritic.HasValue) g.MetacriticScore = details.metacritic.Value;
        if (details.releaseYear.HasValue) g.ReleaseYear = details.releaseYear.Value;
        if (!string.IsNullOrWhiteSpace(details.genres)) g.Genres = details.genres;
        if (!string.IsNullOrWhiteSpace(details.categories)) g.Categories = details.categories;

        // Store reviews summary
        var reviews = await _steam.GetReviewSummaryAsync(g.AppId);
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
