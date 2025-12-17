using MongoDB.Driver;

namespace SteamRec.Functions.Services;

public class GameRepository
{
    private readonly IMongoCollection<GameDocument> _col;

    public GameRepository(MongoDb mongo)
{
    _col = mongo.Database.GetCollection<GameDocument>("games");

    // Unique index on AppId (matches your DB)
    var idx = Builders<GameDocument>.IndexKeys.Ascending(x => x.AppId);
    _col.Indexes.CreateOne(new CreateIndexModel<GameDocument>(
        idx,
        new CreateIndexOptions { Unique = true }
    ));
}

    public Task<List<GameDocument>> GetBatchToRefreshAsync(int batchSize)
    {
        // Oldest updated first
        return _col.Find(Builders<GameDocument>.Filter.Empty)
            .SortBy(x => x.UpdatedUtc)
            .Limit(batchSize)
            .ToListAsync();
    }

    public Task UpsertAsync(GameDocument doc)
    {
        var filter = Builders<GameDocument>.Filter.Eq(x => x.AppId, doc.AppId);
        return _col.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true });
    }
}
