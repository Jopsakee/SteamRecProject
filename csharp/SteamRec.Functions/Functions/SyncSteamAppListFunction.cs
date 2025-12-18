using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SteamRec.Functions.Services;

namespace SteamRec.Functions.Functions;

public class SyncSteamAppListFunction
{
    private readonly ILogger _log;
    private readonly SteamAppListClient _client;
    private readonly SteamAppRepository _repo;
    private readonly IConfiguration _config;

    public SyncSteamAppListFunction(
        ILoggerFactory loggerFactory,
        SteamAppListClient client,
        SteamAppRepository repo,
        IConfiguration config)
    {
        _log = loggerFactory.CreateLogger<SyncSteamAppListFunction>();
        _client = client;
        _repo = repo;
        _config = config;
    }

    [Function("SyncSteamAppList")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", "get")] HttpRequestData req,
        CancellationToken ct)
    {
        var started = DateTime.UtcNow;

        // Tune these in Azure App Settings if needed
        var pageSize = int.TryParse(_config["SteamRec:AppListPageSize"], out var ps) ? ps : 50000;
        var maxPages = int.TryParse(_config["SteamRec:AppListMaxPagesPerRun"], out var mp) ? mp : 10;
        var budgetSec = int.TryParse(_config["SteamRec:AppListTimeBudgetSeconds"], out var tb) ? tb : 240;

        _log.LogInformation("[SyncSteamAppList] Starting. pageSize={pageSize}, maxPages={maxPages}, budgetSec={budgetSec}, utcNow={utcNow}",
            pageSize, maxPages, budgetSec, started);

        try
        {
            int lastAppId = 0;
            bool haveMore = true;

            int pages = 0;
            int totalUpserted = 0;

            while (haveMore && pages < maxPages && (DateTime.UtcNow - started).TotalSeconds < budgetSec)
            {
                var (apps, more, newLast) = await _client.GetAppListPageAsync(
                    lastAppId: lastAppId,
                    maxResults: pageSize,
                    includeGames: true,
                    includeDlc: false,
                    includeSoftware: false,
                    includeVideos: false,
                    includeHardware: false,
                    ifModifiedSince: null,
                    ct: ct);

                if (apps.Count == 0)
                {
                    _log.LogWarning("[SyncSteamAppList] Received 0 apps; stopping. lastAppId={lastAppId}", lastAppId);
                    break;
                }

                await _repo.UpsertFromAppListAsync(apps);

                totalUpserted += apps.Count;
                pages += 1;
                lastAppId = newLast;
                haveMore = more;

                _log.LogInformation("[SyncSteamAppList] Page {page} upserted {count}. lastAppId={lastAppId}, haveMore={haveMore}",
                    pages, apps.Count, lastAppId, haveMore);
            }

            var elapsed = (DateTime.UtcNow - started).TotalSeconds;
            _log.LogInformation("[SyncSteamAppList] Done. pages={pages}, totalUpserted={total}, elapsedSec={sec}",
                pages, totalUpserted, elapsed);

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteStringAsync($"OK. pages={pages}, totalUpserted={totalUpserted}, elapsedSec={elapsed:0.0}");
            return res;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[SyncSteamAppList] Failed.");

            var res = req.CreateResponse(HttpStatusCode.InternalServerError);
            await res.WriteStringAsync(ex.ToString());
            return res;
        }
    }
}
