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

    // Run daily (or whatever you set). This keeps steam_apps updated with new appids.
    [Function("SyncSteamAppList")]
    public async Task RunTimer([TimerTrigger("%SteamRecAppListSchedule%")] object timerInfo)
        => await RunInternalAsync("timer");

    // Manual run from the portal: open this function and click Test/Run (HTTP trigger).
    [Function("SyncSteamAppList_Manual")]
    public async Task<HttpResponseData> RunHttp(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "sync-app-list")] HttpRequestData req)
    {
        await RunInternalAsync("http");
        var res = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await res.WriteStringAsync("OK - steam_apps synced");
        return res;
    }

    private async Task RunInternalAsync(string reason)
    {
        _log.LogInformation("[SyncSteamAppList] Start ({reason}) utcNow={utcNow}", reason, DateTime.UtcNow);

        var apps = await _client.GetAllAppsAsync();
        _log.LogInformation("[SyncSteamAppList] Downloaded {count} apps from Steam.", apps.Count);

        // Upsert to steam_apps
        await _repo.UpsertFromAppListAsync(apps);

        _log.LogInformation("[SyncSteamAppList] Done utcNow={utcNow}", DateTime.UtcNow);
    }
}
