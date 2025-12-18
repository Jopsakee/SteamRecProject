using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SteamRec.Functions.Services;

namespace SteamRec.Functions.Functions;

public class DiscoverSteamAppsFunction
{
    private readonly ILogger _log;
    private readonly SteamWebApiClient _steamWeb;
    private readonly SteamAppRepository _apps;
    private readonly IConfiguration _config;

    public DiscoverSteamAppsFunction(
        ILoggerFactory loggerFactory,
        SteamWebApiClient steamWeb,
        SteamAppRepository apps,
        IConfiguration config)
    {
        _log = loggerFactory.CreateLogger<DiscoverSteamAppsFunction>();
        _steamWeb = steamWeb;
        _apps = apps;
        _config = config;
    }

    [Function("DiscoverSteamApps")]
    public async Task Run([TimerTrigger("%SteamRecDiscoverySchedule%")] object timerInfo)
    {
        _log.LogInformation("[DiscoverSteamApps] Starting discovery utcNow={utcNow}", DateTime.UtcNow);

        var list = await _steamWeb.GetAppListAsync();
        _log.LogInformation("[DiscoverSteamApps] Got app list size={count}", list.Count);

        // Upsert into steam_apps
        await _apps.UpsertFromAppListAsync(list);

        _log.LogInformation("[DiscoverSteamApps] Upserted discovery index OK.");
    }
}
