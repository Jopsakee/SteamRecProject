using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamRec.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddHttpClient();

        services.AddSingleton<MongoDb>();
        services.AddSingleton<GameRepository>();
        services.AddSingleton<SteamStoreClient>();
    })
    .Build();

host.Run();
