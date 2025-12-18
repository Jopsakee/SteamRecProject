using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamRec.Functions.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        // Named clients so we can enable automatic decompression (fixes gzipped body snippets)
        services.AddHttpClient("steam")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            });

        services.AddHttpClient("steamspy")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            });

        services.AddSingleton<MongoDb>();

        services.AddSingleton<GameRepository>();
        services.AddSingleton<SteamAppRepository>();

        services.AddSingleton<SteamStoreClient>();
        services.AddSingleton<SteamAppListClient>();
        services.AddSingleton<SteamSpyClient>();
    })
    .Build();

host.Run();
