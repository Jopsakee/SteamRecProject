using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SteamRec.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddHttpClient();

        services.AddSingleton<MongoDb>();
        services.AddSingleton<GameRepository>();
        services.AddSingleton<SteamAppRepository>();

        services.AddSingleton<SteamStoreClient>();
        services.AddSingleton<SteamAppListClient>();
        builder.Services.AddSingleton<SteamWebApiClient>();
    })
    .Build();

host.Run();
