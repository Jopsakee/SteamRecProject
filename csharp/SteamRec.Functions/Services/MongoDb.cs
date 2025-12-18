using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace SteamRec.Functions.Services;

public class MongoDb
{
    public IMongoDatabase Database { get; }

    public MongoDb(IConfiguration config)
    {
        // Azure App Settings:
        // Mongo__ConnectionString OR Mongo:ConnectionString
        // Mongo__Database OR Mongo:Database
        var cs = config["Mongo:ConnectionString"] ?? config["Mongo__ConnectionString"]
                 ?? Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");

        var dbName = config["Mongo:Database"] ?? config["Mongo__Database"]
                     ?? Environment.GetEnvironmentVariable("MONGODB_DATABASE")
                     ?? "steamrec";

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "Mongo connection string not configured. Set Mongo__ConnectionString (App Settings) or MONGODB_CONNECTION_STRING.");

        var client = new MongoClient(cs);
        Database = client.GetDatabase(dbName);
    }
}
