using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamRec.Core;

namespace SteamRec.Web.Pages;

public class StatsModel : PageModel
{
    private readonly ContentBasedRecommender _recommender;

    public StatsModel(ContentBasedRecommender recommender)
    {
        _recommender = recommender;
    }

    public int TotalGames { get; private set; }
    public int FreeGames { get; private set; }
    public double AveragePrice { get; private set; }
    public double AverageMetacritic { get; private set; }
    public List<ChartPoint> TopTags { get; private set; } = new();
    public List<ChartPoint> TopPlayedGames { get; private set; } = new();
    public List<ChartPoint> ReleaseYears { get; private set; } = new();
    public List<ChartPoint> PriceBuckets { get; private set; } = new();

    public void OnGet()
    {
        var games = _recommender.Games;
        TotalGames = games.Count;
        FreeGames = games.Count(g => g.IsFree || g.PriceEur <= 0);

        var paidGames = games.Where(g => !g.IsFree && g.PriceEur > 0).ToList();
        AveragePrice = paidGames.Count == 0 ? 0 : paidGames.Average(g => g.PriceEur);

        var metacriticScores = games.Where(g => g.MetacriticScore > 0).Select(g => g.MetacriticScore).ToList();
        AverageMetacritic = metacriticScores.Count == 0 ? 0 : metacriticScores.Average();

        TopTags = BuildTopCounts(games, g => g.Tags, 10);
        TopPlayedGames = BuildTopPlayedGames(games, 10);
        ReleaseYears = BuildReleaseYearCounts(games, 2025);
        PriceBuckets = BuildPriceBuckets(games);
    }

    private static List<ChartPoint> BuildTopCounts(
        IEnumerable<GameRecord> games,
        Func<GameRecord, IEnumerable<string>> selector,
        int take)
    {
        return games
            .SelectMany(selector)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .GroupBy(label => label, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ChartPoint(group.Key, group.Count()))
            .OrderByDescending(point => point.Count)
            .ThenBy(point => point.Label, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    private static List<ChartPoint> BuildTopPlayedGames(IEnumerable<GameRecord> games, int take)
    {
        return games
            .OrderByDescending(g => g.ReviewTotal)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(g => new ChartPoint(g.Name, g.ReviewTotal))
            .ToList();
    }


    private static List<ChartPoint> BuildReleaseYearCounts(
        IEnumerable<GameRecord> games,
        int endYear)
    {
        var yearCounts = games
            .Where(g => g.ReleaseYear > 0)
            .GroupBy(g => g.ReleaseYear)
            .ToDictionary(group => group.Key, group => group.Count());

        if (yearCounts.Count == 0)
            return new List<ChartPoint>();

        var minYear = yearCounts.Keys.Min();
        var lastYear = Math.Max(endYear, minYear);
        var points = new List<ChartPoint>();

        for (var year = minYear; year <= lastYear; year++)
        {
            yearCounts.TryGetValue(year, out var count);
            points.Add(new ChartPoint(year.ToString(), count));
        }

        return points;
    }

    private static List<ChartPoint> BuildPriceBuckets(IEnumerable<GameRecord> games)
    {
        var buckets = new Dictionary<string, int>
        {
            ["Free"] = 0,
            ["Under €5"] = 0,
            ["€5-€15"] = 0,
            ["€15-€30"] = 0,
            ["€30-€60"] = 0,
            ["€60+"] = 0
        };

        foreach (var game in games)
        {
            if (game.IsFree || game.PriceEur <= 0)
            {
                buckets["Free"]++;
                continue;
            }

            var price = game.PriceEur;
            if (price < 5)
                buckets["Under €5"]++;
            else if (price < 15)
                buckets["€5-€15"]++;
            else if (price < 30)
                buckets["€15-€30"]++;
            else if (price < 60)
                buckets["€30-€60"]++;
            else
                buckets["€60+"]++;
        }

        return buckets.Select(kv => new ChartPoint(kv.Key, kv.Value)).ToList();
    }

    public record ChartPoint(string Label, int Count);
}