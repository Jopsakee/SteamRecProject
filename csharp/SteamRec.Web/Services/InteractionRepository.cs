using MongoDB.Driver;

namespace SteamRec.Web.Services;

public class InteractionRepository
{
    private readonly IMongoCollection<InteractionDocument> _col;

    public InteractionRepository(MongoDb mongo)
    {
        _col = mongo.Database.GetCollection<InteractionDocument>("interactions");

        // One row per (SteamId, AppId)
        var idx = Builders<InteractionDocument>.IndexKeys
            .Ascending(x => x.SteamId)
            .Ascending(x => x.AppId);

        _col.Indexes.CreateOne(new CreateIndexModel<InteractionDocument>(idx));
    }

    public async Task UpsertManyAsync(string steamId, IEnumerable<InteractionDocument> docs)
    {
        var list = docs.ToList();
        if (list.Count == 0) return;

        var models = new List<WriteModel<InteractionDocument>>(list.Count);

        foreach (var d in list)
        {
            // Filter by natural key
            var filter = Builders<InteractionDocument>.Filter.Where(x => x.SteamId == steamId && x.AppId == d.AppId);

            // Update fields (do NOT touch _id)
            var update = Builders<InteractionDocument>.Update
                .Set(x => x.SteamId, steamId)
                .Set(x => x.AppId, d.AppId)
                .Set(x => x.PlaytimeForever, d.PlaytimeForever)
                .Set(x => x.Playtime2Weeks, d.Playtime2Weeks)
                .Set(x => x.UpdatedUtc, d.UpdatedUtc);

            models.Add(new UpdateOneModel<InteractionDocument>(filter, update) { IsUpsert = true });
        }

        await _col.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false });
    }

    public Task<List<InteractionDocument>> GetAllAsync()
        => _col.Find(Builders<InteractionDocument>.Filter.Empty).ToListAsync();
}
