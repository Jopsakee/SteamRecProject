using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamRec.Core;

public class ContentBasedRecommender
{
    private readonly List<GameRecord> _games;
    private readonly Dictionary<int, GameRecord> _byAppId;
    private readonly FeatureBuilder _featureBuilder;

    private readonly double _revMin;
    private readonly double _revMax;
    private readonly double _volMin;
    private readonly double _volMax;

    // Expose number of games loaded
    public int GameCount => _games.Count;

    private enum AxisKind
    {
        Genre,
        Tag,
        Category
    }

    private const double W_SIM = 1.8;
    private const double W_REV = 0.5;
    private const double W_VOL = 0.6;

    public ContentBasedRecommender(List<GameRecord> games)
    {
        _games = games ?? throw new ArgumentNullException(nameof(games));
        _byAppId = games.ToDictionary(g => g.AppId, g => g);

        _featureBuilder = new FeatureBuilder();
        _featureBuilder.BuildFeatures(_games);

        _revMin = _games.Min(g => g.ReviewScoreAdj);
        _revMax = _games.Max(g => g.ReviewScoreAdj);
        _volMin = _games.Min(g => g.ReviewVolumeLog);
        _volMax = _games.Max(g => g.ReviewVolumeLog);
    }

    public IReadOnlyList<GameRecord> Games => _games;

    public class LikedGamePreference
    {
        public int AppId { get; init; }
        public double Weight { get; init; } = 1.0;
    }

    public class AxisInsight
    {
        public string Name { get; init; } = "";
        public string Source { get; init; } = "";
        public double UserScore { get; init; }
        public double GameScore { get; init; }
    }

    public class InfluenceTrace
    {
        public int AppId { get; init; }
        public string Name { get; init; } = "";
        public double Similarity { get; init; }
        public double InfluenceScore { get; init; }
        public double Weight { get; init; }
    }

    public class RecommendationWithInsights
    {
        public GameRecord Game { get; init; } = new();
        public double Similarity { get; init; }
        public double OverallScore { get; init; }
        public List<AxisInsight> Axes { get; init; } = new();
        public List<InfluenceTrace> Influencers { get; init; } = new();
    }

    public List<(GameRecord game, double similarity, double overallScore)> RecommendSimilar(
        int appId,
        int topN = 10,
        int maxCandidates = 200)
    {
        if (!_byAppId.TryGetValue(appId, out var target))
            throw new ArgumentException($"Unknown appId {appId}");

        var refGenres = target.Genres;
        var targetVec = target.Features;

        var candidates = new List<(GameRecord game, double similarity, double overallScore)>();

        foreach (var g in _games)
        {
            if (g.AppId == appId) continue;

            // genre filter
            if (refGenres.Count > 0 && !g.Genres.Overlaps(refGenres))
                continue;

            double sim = CosineSimilarity(targetVec, g.Features);
            candidates.Add((g, sim, 0.0));
        }

        // top by similarity
        candidates = candidates
            .OrderByDescending(c => c.similarity)
            .Take(maxCandidates)
            .ToList();

        // re-ranking with reviews
        for (int i = 0; i < candidates.Count; i++)
        {
            var (g, sim, _) = candidates[i];
            double overall = ComputeOverallScore(sim, g);
            candidates[i] = (g, sim, overall);
        }

        return candidates
            .OrderByDescending(c => c.overallScore)
            .Take(topN)
            .ToList();
    }

    /// Recommend games based on a list of liked appIds (user library).
    public List<(GameRecord game, double similarity, double overallScore)> RecommendForLiked(
        IEnumerable<int> likedAppIds,
        int topN = 20,
        int maxCandidates = 500)
    {
        var likedPreferences = (likedAppIds ?? Enumerable.Empty<int>())
            .Select(id => new LikedGamePreference { AppId = id, Weight = 1.0 })
            .ToList();

        return RecommendForLikedWithInsights(likedPreferences, topN, maxCandidates)
            .Select(r => (r.Game, r.Similarity, r.OverallScore))
            .ToList();
    }

    public List<RecommendationWithInsights> RecommendForLikedWithInsights(
        IEnumerable<LikedGamePreference> likedGames,
        int topN = 20,
        int maxCandidates = 500,
        int axisCount = 6,
        int influencerCount = 3)
    {
        var likedList = likedGames?.ToList() ?? new List<LikedGamePreference>();
        var likedSet = likedList.Select(l => l.AppId).ToHashSet();
        var likedRecords = _games.Where(g => likedSet.Contains(g.AppId)).ToList();

        if (likedRecords.Count == 0)
            throw new ArgumentException("None of the liked appids were found in the dataset.");

        var weightByAppId = likedList.ToDictionary(l => l.AppId, l => Math.Max(1.0, l.Weight));

        // Union of genres across liked games
        var likedGenresUnion = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in likedRecords)
            likedGenresUnion.UnionWith(g.Genres);

        // Weighted average feature vector for liked games
        int dim = likedRecords[0].Features.Length;
        var userVec = new double[dim];
        double totalWeight = 0.0;

        foreach (var g in likedRecords)
        {
            double w = ComputePreferenceWeight(weightByAppId[g.AppId]);
            totalWeight += w;

            for (int i = 0; i < dim; i++)
                userVec[i] += g.Features[i] * w;
        }

        if (totalWeight > 0)
        {
            for (int i = 0; i < dim; i++)
                userVec[i] /= totalWeight;
        }

        var candidates = new List<(GameRecord game, double similarity, double overallScore)>();

        foreach (var g in _games)
        {
            if (likedSet.Contains(g.AppId)) continue;

            // genre filter: must share at least one genre with ANY liked game (if any genres exist)
            if (likedGenresUnion.Count > 0 && !g.Genres.Overlaps(likedGenresUnion))
                continue;

            double sim = CosineSimilarity(userVec, g.Features);
            candidates.Add((g, sim, 0.0));
        }

        // top by similarity
        candidates = candidates
            .OrderByDescending(c => c.similarity)
            .Take(maxCandidates)
            .ToList();

        // re-ranking with reviews
        for (int i = 0; i < candidates.Count; i++)
        {
            var (g, sim, _) = candidates[i];
            double overall = ComputeOverallScore(sim, g);
            candidates[i] = (g, sim, overall);
        }

        var axesTemplate = BuildUserAxes(likedRecords, weightByAppId, axisCount);

        return candidates
            .OrderByDescending(c => c.overallScore)
            .Take(topN)
            .Select(c =>
            {
                var axes = axesTemplate
                    .Select(a => new AxisInsight
                    {
                        Name = a.name,
                        Source = a.kind.ToString(),
                        UserScore = a.score,
                        GameScore = ScoreForGameAxis(c.game, a.kind, a.name)
                    })
                    .ToList();

                var influencers = BuildInfluencers(c.game, likedRecords, weightByAppId, influencerCount);

                return new RecommendationWithInsights
                {
                    Game = c.game,
                    Similarity = c.similarity,
                    OverallScore = c.overallScore,
                    Axes = axes,
                    Influencers = influencers
                };
            })
            .ToList();
    }

    private double ComputeOverallScore(double similarity, GameRecord g)
    {
        double revScore = g.ReviewScoreAdj;
        double revVol = g.ReviewVolumeLog;

        double normRev = Normalize(revScore, _revMin, _revMax, 0.5);
        double normVol = Normalize(revVol, _volMin, _volMax, 0.0);

        return W_SIM * similarity
             + W_REV * normRev
             + W_VOL * normVol;
    }

    private static double CosineSimilarity(double[] a, double[] b)
    {
        double dot = 0.0, normA = 0.0, normB = 0.0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0) return 0.0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static double Normalize(double value, double min, double max, double defaultValue)
    {
        if (max <= min) return defaultValue;
        return (value - min) / (max - min);
    }

    private static double ComputePreferenceWeight(double weight)
    {
        // Use a logarithmic scale to reduce the impact of extreme playtimes but keep ordering.
        return Math.Max(1.0, Math.Log10(weight + 1));
    }

    private static List<(AxisKind kind, string name, double score)> BuildUserAxes(
        List<GameRecord> likedGames,
        Dictionary<int, double> weightByAppId,
        int axisCount)
    {
        var scores = new Dictionary<(AxisKind kind, string name), double>();

        foreach (var g in likedGames)
        {
            double weight = ComputePreferenceWeight(weightByAppId[g.AppId]);

            foreach (var genre in g.Genres)
                AccumulateScore(scores, AxisKind.Genre, genre, weight * 1.2);

            foreach (var tag in g.Tags)
                AccumulateScore(scores, AxisKind.Tag, tag, weight);

            foreach (var cat in g.Categories)
                AccumulateScore(scores, AxisKind.Category, cat, weight * 0.8);
        }

        var top = scores
            .OrderByDescending(kv => kv.Value)
            .Take(axisCount)
            .ToList();

        if (top.Count == 0)
            return new List<(AxisKind, string, double)>();

        double maxScore = top.Max(kv => kv.Value);

        return top
            .Select(kv => (kv.Key.kind, kv.Key.name, maxScore > 0 ? kv.Value / maxScore : 0.0))
            .ToList();
    }

    private static void AccumulateScore(
        Dictionary<(AxisKind kind, string name), double> scores,
        AxisKind kind,
        string name,
        double value)
    {
        var key = (kind, name);
        if (!scores.ContainsKey(key))
            scores[key] = 0.0;

        scores[key] += value;
    }

    private static double ScoreForGameAxis(GameRecord game, AxisKind kind, string name)
    {
        return kind switch
        {
            AxisKind.Genre => game.Genres.Contains(name) ? 1.0 : 0.0,
            AxisKind.Tag => game.Tags.Contains(name) ? 1.0 : 0.0,
            AxisKind.Category => game.Categories.Contains(name) ? 1.0 : 0.0,
            _ => 0.0
        };
    }

    private static List<InfluenceTrace> BuildInfluencers(
        GameRecord candidate,
        List<GameRecord> likedGames,
        Dictionary<int, double> weightByAppId,
        int influencerCount)
    {
        return likedGames
            .Select(g =>
            {
                double similarity = CosineSimilarity(candidate.Features, g.Features);
                double weight = weightByAppId[g.AppId];
                double influenceScore = similarity * ComputePreferenceWeight(weight);

                return new InfluenceTrace
                {
                    AppId = g.AppId,
                    Name = g.Name,
                    Similarity = similarity,
                    InfluenceScore = influenceScore,
                    Weight = weight
                };
            })
            .OrderByDescending(i => i.InfluenceScore)
            .Take(influencerCount)
            .ToList();
    }
}
