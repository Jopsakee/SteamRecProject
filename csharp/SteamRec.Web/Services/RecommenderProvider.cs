using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SteamRec.Core;

namespace SteamRec.Web.Services;

public interface IRecommenderProvider
{
    Task<ContentBasedRecommender> GetContentBasedAsync();
}

public sealed class RecommenderProvider : IRecommenderProvider
{
    private readonly GameRepository _repository;
    private readonly Lazy<Task<ContentBasedRecommender>> _contentBased;

    public RecommenderProvider(GameRepository repository)
    {
        _repository = repository;
        _contentBased = new Lazy<Task<ContentBasedRecommender>>(LoadContentBasedAsync);
    }

    public Task<ContentBasedRecommender> GetContentBasedAsync() => _contentBased.Value;

    private async Task<ContentBasedRecommender> LoadContentBasedAsync()
    {
        Console.WriteLine("[SteamRec] Loading games from MongoDB (streamed)...");

        var games = new List<GameRecord>(capacity: 150_000);

        await foreach (var doc in _repository.StreamAllAsync())
        {
            var game = new GameRecord
            {
                AppId = doc.AppId,
                Name = doc.Name ?? "",
                GenresRaw = doc.Genres ?? "",
                CategoriesRaw = doc.Categories ?? "",
                TagsRaw = doc.Tags ?? "",
                PriceEur = doc.PriceEur,
                MetacriticScore = doc.MetacriticScore,
                ReleaseYear = doc.ReleaseYear,
                RequiredAge = doc.RequiredAge,
                IsFree = doc.IsFree,
                ReviewTotal = doc.ReviewTotal,
                ReviewRatio = doc.ReviewRatio,
                ReviewScoreAdj = doc.ReviewScoreAdj,
                FullGameAppId = doc.FullGameAppId
            };

            game.Genres = SplitSemicolon(game.GenresRaw);
            game.Categories = SplitSemicolon(game.CategoriesRaw);
            game.Tags = SplitSemicolon(game.TagsRaw);

            games.Add(game);
        }

        if (games.Count == 0)
            throw new InvalidOperationException("[SteamRec] MongoDB returned 0 games. Did you import games into the 'games' collection?");

        Console.WriteLine($"[SteamRec] Loaded {games.Count} games from MongoDB.");

        return new ContentBasedRecommender(games);
    }

    private static HashSet<string> SplitSemicolon(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}