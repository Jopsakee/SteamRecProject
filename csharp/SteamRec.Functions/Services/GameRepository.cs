using MongoDB.Driver;

namespace SteamRec.Functions.Services;

public class GameRepository
{
    private readonly IMongoCollection<GameDocument> _col;

    public GameRepository(MongoDb mongo)
    {
        _col = mongo.Database.GetCollection<GameDocument>("games");

        // Unique index on AppId (compatible with older MongoDB.Driver)
        // NOTE: This will fail if you have any documents where AppId is missing/0.
        var idx = Builders<GameDocument>.IndexKeys.Ascending(x => x.AppId);

        try
        {
            _col.Indexes.CreateOne(new CreateIndexModel<GameDocument>(
                idx,
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "AppId_1"
                }));
        }
        catch
        {
            // If it already exists or fails due to existing bad docs, the function will still run.
            // You can check/fix the collection then redeploy.
        }
    }

    public Task<List<GameDocument>> GetStaleBatchAsync(int batchSize, DateTime staleBeforeUtc)
    {
        // Only refresh docs that have a valid AppId and are stale
        var filter =
            Builders<GameDocument>.Filter.Gt(x => x.AppId, 0) &
            Builders<GameDocument>.Filter.Lt(x => x.UpdatedUtc, staleBeforeUtc);

        return _col.Find(filter)
            .SortBy(x => x.UpdatedUtc)
            .Limit(batchSize)
            .ToListAsync();
    }

    public Task UpsertAsync(GameDocument doc)
    {
        if (doc.AppId <= 0) return Task.CompletedTask;

        var filter = Builders<GameDocument>.Filter.Eq(x => x.AppId, doc.AppId);
        return _col.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true });
    }

    public Task<bool> ExistsAsync(int appId)
    {
        var filter = Builders<GameDocument>.Filter.Eq(x => x.AppId, appId);
        return _col.Find(filter).Limit(1).AnyAsync();
    }
}
