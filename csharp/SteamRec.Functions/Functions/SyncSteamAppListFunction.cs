using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using SteamRec.Functions.Services;

namespace SteamRec.Functions.Functions;

public class SyncSteamAppListFunction
{
    private readonly ILogger _log;
    private readonly SteamAppListClient _client;
    private readonly SteamAppRepository _repo;

    public SyncSteamAppListFunction(
        ILoggerFactory loggerFactory,
        SteamAppListClient client,
        SteamAppRepository repo)
    {
        _log = loggerFactory.CreateLogger<SyncSteamAppListFunction>();
        _client = client;
        _repo = repo;
    }

    // Manual run (Portal Test/Run or HTTP call)
    [Function("SyncSteamAppList")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", "get")] HttpRequestData req)
    {
        var started = DateTime.UtcNow;
        _log.LogInformation("[SyncSteamAppList] Starting. utcNow={utcNow}", started);

        var apps = await _client.GetAppListAsync();
        _log.LogInformation("[SyncSteamAppList] Fetched app list count={count}", apps.Count);

        await _repo.UpsertFromAppListAsync(apps.Select(a => (a.appId, a.name)));

        var elapsed = (DateTime.UtcNow - started).TotalSeconds;
        _log.LogInformation("[SyncSteamAppList] Done. elapsedSec={sec}", elapsed);

        var res = req.CreateResponse(HttpStatusCode.OK);
        await res.WriteStringAsync($"OK. Synced {apps.Count} apps. elapsedSec={elapsed:0.0}");
        return res;
    }
}
