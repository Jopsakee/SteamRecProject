using MongoDB.Driver;

namespace SteamRec.Functions.Services;

public class MongoDb
{
    public IMongoDatabase Database { get; }

    public MongoDb(IConfiguration config)
    {
        // Azure Functions: app settings like Mongo__ConnectionString map to Mongo:ConnectionString automatically
        var cs = config["Mongo:ConnectionString"] ?? Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
        var dbName = config["Mongo:Database"] ?? Environment.GetEnvironmentVariable("MONGODB_DATABASE") ?? "steamrec";

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Mongo connection string not configured. Set Mongo__ConnectionString (recommended) or MONGODB_CONNECTION_STRING.");

        var client = new MongoClient(cs);
        Database = client.GetDatabase(dbName);
    }
}
