using MongoDB.Driver;

namespace SteamRec.Web.Services;

public class MongoDb
{
    public IMongoDatabase Database { get; }

    public MongoDb(IConfiguration config)
    {
        // Local dev: use user-secrets (Mongo:ConnectionString), or env var (MONGODB_CONNECTION_STRING)
        // Azure App Service: use App Settings Mongo__ConnectionString / Mongo__Database
        var cs = config["Mongo:ConnectionString"] ?? Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
        var dbName = config["Mongo:Database"] ?? Environment.GetEnvironmentVariable("MONGODB_DATABASE") ?? "steamrec";

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "Mongo connection string not configured. Set Mongo:ConnectionString (user-secrets) or MONGODB_CONNECTION_STRING.");

        var client = new MongoClient(cs);
        Database = client.GetDatabase(dbName);
    }
}
