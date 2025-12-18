using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamRec.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Named HttpClient so we reliably get an IHttpClientBuilder
        services.AddHttpClient("steam")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });

        services.AddSingleton<MongoDb>();

        services.AddSingleton<GameRepository>();
        services.AddSingleton<SteamAppRepository>();

        services.AddSingleton<SteamStoreClient>();
        services.AddSingleton<SteamAppListClient>();
    })
    .Build();

host.Run();
