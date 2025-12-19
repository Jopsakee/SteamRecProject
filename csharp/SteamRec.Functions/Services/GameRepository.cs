using MongoDB.Driver;

namespace SteamRec.Functions.Services;

public class GameRepository
{
    private readonly IMongoCollection<GameDocument> _col;

    public GameRepository(MongoDb mongo)
    {
        _col = mongo.Database.GetCollection<GameDocument>("games");

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
            // Ignore if exists already.
        }
    }

    public Task<List<GameDocument>> GetStaleBatchAsync(int batchSize, DateTime staleBeforeUtc)
    {
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

        var update = Builders<GameDocument>.Update
            .SetOnInsert(x => x.AppId, doc.AppId)
            .Set(x => x.Name, doc.Name)

            .Set(x => x.Genres, doc.Genres)
            .Set(x => x.Categories, doc.Categories)
            .Set(x => x.Tags, doc.Tags)

            .Set(x => x.PriceEur, doc.PriceEur)
            .Set(x => x.MetacriticScore, doc.MetacriticScore)
            .Set(x => x.ReleaseYear, doc.ReleaseYear)
            .Set(x => x.RequiredAge, doc.RequiredAge)
            .Set(x => x.IsFree, doc.IsFree)

            .Set(x => x.ReviewPositive, doc.ReviewPositive)
            .Set(x => x.ReviewNegative, doc.ReviewNegative)
            .Set(x => x.ReviewTotal, doc.ReviewTotal)
            .Set(x => x.ReviewRatio, doc.ReviewRatio)
            .Set(x => x.ReviewScoreAdj, doc.ReviewScoreAdj)
            .Set(x => x.ReviewVolumeLog, doc.ReviewVolumeLog)

            .Set(x => x.UpdatedUtc, doc.UpdatedUtc);

        return _col.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
    }

    public Task<bool> ExistsAsync(int appId)
    {
        var filter = Builders<GameDocument>.Filter.Eq(x => x.AppId, appId);
        return _col.Find(filter).Limit(1).AnyAsync();
    }
}
