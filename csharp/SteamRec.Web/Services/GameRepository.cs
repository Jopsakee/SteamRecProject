using MongoDB.Driver;

namespace SteamRec.Web.Services;

public class GameRepository
{
    private readonly IMongoCollection<GameDocument> _games;

    public GameRepository(MongoDb mongo)
    {
        _games = mongo.Database.GetCollection<GameDocument>("games");
    }

    public Task<List<GameDocument>> GetAllAsync()
        => _games.Find(Builders<GameDocument>.Filter.Empty).ToListAsync();
}
