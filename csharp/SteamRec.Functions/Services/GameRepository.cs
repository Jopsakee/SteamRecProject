using MongoDB.Driver;

namespace SteamRec.Functions.Services;

public class GameRepository
{
    private readonly IMongoCollection<GameDocument> _col;

    public GameRepository(MongoDb mongo)
    {
        _col = mongo.Database.GetCollection<GameDocument>("games");

        // Unique index on AppId, but only for valid AppId > 0 (avoids null/0 legacy docs killing index builds)
        var idx = Builders<GameDocument>.IndexKeys.Ascending(x => x.AppId);
        var opts = new CreateIndexOptions
        {
            Unique = true,
            PartialFilterExpression = Builders<GameDocument>.Filter.Gt(x => x.AppId, 0)
        };
        _col.Indexes.CreateOne(new CreateIndexModel<GameDocument>(idx, opts));
    }

    public Task<List<GameDocument>> GetStaleBatchAsync(int batchSize, DateTime staleBeforeUtc)
    {
        // refresh oldest first, only those that are stale enough
        var filter = Builders<GameDocument>.Filter.Lt(x => x.UpdatedUtc, staleBeforeUtc)
                    & Builders<GameDocument>.Filter.Gt(x => x.AppId, 0);

        return _col.Find(filter)
            .SortBy(x => x.UpdatedUtc)
            .Limit(batchSize)
            .ToListAsync();
    }

    public Task<bool> ExistsAsync(int appId)
    {
        var filter = Builders<GameDocument>.Filter.Eq(x => x.AppId, appId);
        return _col.Find(filter).Limit(1).AnyAsync();
    }

    public Task UpsertAsync(GameDocument doc)
    {
        if (doc.AppId <= 0) return Task.CompletedTask;

        var filter = Builders<GameDocument>.Filter.Eq(x => x.AppId, doc.AppId);
        return _col.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true });
    }
}
