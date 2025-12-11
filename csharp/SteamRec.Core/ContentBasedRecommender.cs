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

    public int GameCount => _games.Count;

    // Match these to your Python W_SIM, W_REV, W_VOL
    private const double W_SIM = 1.8;
    private const double W_REV = 0.5;
    private const double W_VOL = 0.6;

    public ContentBasedRecommender(List<GameRecord> games)
    {
        _games = games;
        _byAppId = games.ToDictionary(g => g.AppId, g => g);

        _featureBuilder = new FeatureBuilder();
        _featureBuilder.BuildFeatures(_games);

        _revMin = _games.Min(g => g.ReviewScoreAdj);
        _revMax = _games.Max(g => g.ReviewScoreAdj);
        _volMin = _games.Min(g => g.ReviewVolumeLog);
        _volMax = _games.Max(g => g.ReviewVolumeLog);
    }

    public IReadOnlyList<GameRecord> Games => _games;

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
            double revScore = g.ReviewScoreAdj;
            double revVol = g.ReviewVolumeLog;

            double normRev = Normalize(revScore, _revMin, _revMax, 0.5);
            double normVol = Normalize(revVol, _volMin, _volMax, 0.0);

            double overall = W_SIM * sim
                           + W_REV * normRev
                           + W_VOL * normVol;

            candidates[i] = (g, sim, overall);
        }

        return candidates
            .OrderByDescending(c => c.overallScore)
            .Take(topN)
            .ToList();
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
}
