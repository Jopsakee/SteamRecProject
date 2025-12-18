using MongoDB.Driver;

namespace SteamRec.Functions.Services;

public class SteamAppRepository
{
    private readonly IMongoCollection<SteamAppDocument> _col;

    public SteamAppRepository(MongoDb mongo)
    {
        _col = mongo.Database.GetCollection<SteamAppDocument>("steam_apps");

        // Unique index on AppId (compatible with older MongoDB.Driver)
        var idx = Builders<SteamAppDocument>.IndexKeys.Ascending(x => x.AppId);

        try
        {
            _col.Indexes.CreateOne(new CreateIndexModel<SteamAppDocument>(
                idx,
                new CreateIndexOptions
                {
                    Unique = true,
                    Name = "AppId_1"
                }));
        }
        catch
        {
            // Ignore if already exists or fails due to bad docs
        }

        try
        {
            _col.Indexes.CreateOne(new CreateIndexModel<SteamAppDocument>(
                Builders<SteamAppDocument>.IndexKeys.Ascending(x => x.HydratedUtc),
                new CreateIndexOptions { Name = "HydratedUtc_1" }));
        }
        catch { }

        try
        {
            _col.Indexes.CreateOne(new CreateIndexModel<SteamAppDocument>(
                Builders<SteamAppDocument>.IndexKeys.Ascending(x => x.NextAttemptUtc),
                new CreateIndexOptions { Name = "NextAttemptUtc_1" }));
        }
        catch { }
    }

    public async Task UpsertFromAppListAsync(IEnumerable<(int appId, string name)> apps)
    {
        var now = DateTime.UtcNow;

        var list = apps
            .Where(a => a.appId > 0)
            .Select(a => (a.appId, name: a.name ?? ""))
            .ToList();

        if (list.Count == 0) return;

        var models = list.Select(a =>
        {
            var filter = Builders<SteamAppDocument>.Filter.Eq(x => x.AppId, a.appId);
            var update = Builders<SteamAppDocument>.Update
                .SetOnInsert(x => x.AppId, a.appId)
                .Set(x => x.Name, a.name)
                .Set(x => x.LastSeenUtc, now);

            return new UpdateOneModel<SteamAppDocument>(filter, update) { IsUpsert = true };
        }).ToList();

        await _col.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false });
    }

    public Task<List<SteamAppDocument>> GetNextToHydrateAsync(int batchSize)
    {
        var now = DateTime.UtcNow;

        var filter =
            Builders<SteamAppDocument>.Filter.Eq(x => x.HydratedUtc, null) &
            (Builders<SteamAppDocument>.Filter.Eq(x => x.NextAttemptUtc, null) |
             Builders<SteamAppDocument>.Filter.Lte(x => x.NextAttemptUtc, now)) &
            Builders<SteamAppDocument>.Filter.Gt(x => x.AppId, 0);

        return _col.Find(filter)
            .SortBy(x => x.FailureCount)
            .ThenBy(x => x.LastAttemptUtc)
            .Limit(batchSize)
            .ToListAsync();
    }

    public Task MarkHydratedAsync(int appId)
    {
        var now = DateTime.UtcNow;

        var filter = Builders<SteamAppDocument>.Filter.Eq(x => x.AppId, appId);
        var update = Builders<SteamAppDocument>.Update
            .Set(x => x.HydratedUtc, now)
            .Set(x => x.LastAttemptUtc, now)
            .Set(x => x.NextAttemptUtc, null)
            .Set(x => x.FailureCount, 0);

        return _col.UpdateOneAsync(filter, update);
    }

    public Task MarkFailedAsync(int appId, int failureCountAfterIncrement)
    {
        var now = DateTime.UtcNow;

        var minutes = Math.Min(6 * 60, (int)(5 * Math.Pow(2, Math.Min(10, failureCountAfterIncrement - 1))));
        var next = now.AddMinutes(minutes);

        var filter = Builders<SteamAppDocument>.Filter.Eq(x => x.AppId, appId);
        var update = Builders<SteamAppDocument>.Update
            .Inc(x => x.FailureCount, 1)
            .Set(x => x.LastAttemptUtc, now)
            .Set(x => x.NextAttemptUtc, next);

        return _col.UpdateOneAsync(filter, update);
    }

    public async Task<int> GetFailureCountAsync(int appId)
    {
        var filter = Builders<SteamAppDocument>.Filter.Eq(x => x.AppId, appId);
        var doc = await _col.Find(filter).FirstOrDefaultAsync();
        return doc?.FailureCount ?? 0;
    }
}
