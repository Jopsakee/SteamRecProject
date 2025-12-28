using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
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

    public async IAsyncEnumerable<GameDocument> StreamAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var cursor = await _games
            .Find(Builders<GameDocument>.Filter.Empty)
            .ToCursorAsync(cancellationToken);

        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var doc in cursor.Current)
                yield return doc;
        }
    }
}
