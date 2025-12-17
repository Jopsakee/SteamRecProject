using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;

namespace SteamRec.ML;

public class CollaborativeFilteringRecommender
{
    private readonly MLContext _ml = new(seed: 42);
    private ITransformer? _model;

    public bool IsReady => _model != null;
    public string? LastError { get; private set; }

    private HashSet<uint> _knownAppIds = new();
    private HashSet<string> _knownUsers = new(StringComparer.Ordinal);

    private const float RECENT_BONUS = 0.35f;

    // Must match (or be <=) the writer’s filter
    private const int MIN_TOTAL_MINUTES = 10;

    public void TrainFromCsv(string interactionsCsvPath)
    {
        try
        {
            LastError = null;

            if (!File.Exists(interactionsCsvPath))
                throw new FileNotFoundException("interactions.csv not found", interactionsCsvPath);

            var rawDv = _ml.Data.LoadFromTextFile<InteractionRaw>(
                path: interactionsCsvPath,
                hasHeader: true,
                separatorChar: ',');

            var raw = _ml.Data.CreateEnumerable<InteractionRaw>(rawDv, reuseRowObject: false)
                .Where(r => !string.IsNullOrWhiteSpace(r.SteamId))
                .Where(r => r.AppId > 0)
                .ToList();

            if (raw.Count == 0)
                throw new InvalidOperationException("interactions.csv loaded but contains 0 valid rows.");

            var trainRows = raw
                .Where(r => (Math.Max(0, (int)r.PlaytimeForever) + Math.Max(0, (int)r.Playtime2Weeks)) >= MIN_TOTAL_MINUTES)
                .Select(r => new InteractionTrain
                {
                    UserId = r.SteamId.Trim(),
                    AppId = r.AppId,
                    Rating = ComputeImplicitRating(r.PlaytimeForever, r.Playtime2Weeks)
                })
                .Where(x => x.Rating > 0f)
                .ToList();

            _knownAppIds = trainRows.Select(x => x.AppId).ToHashSet();
            _knownUsers = trainRows.Select(x => x.UserId).ToHashSet(StringComparer.Ordinal);

            if (_knownUsers.Count < 2 || _knownAppIds.Count < 50)
                throw new InvalidOperationException(
                    $"Not enough interaction data for CF. Users={_knownUsers.Count}, Items={_knownAppIds.Count}. " +
                    $"Add more SteamIDs and played games to interactions.csv.");

            var trainDv = _ml.Data.LoadFromEnumerable(trainRows);

            var pipeline =
                _ml.Transforms.Conversion.MapValueToKey("userIdEncoded", nameof(InteractionTrain.UserId))
                .Append(_ml.Transforms.Conversion.MapValueToKey("appIdEncoded", nameof(InteractionTrain.AppId)))
                .Append(_ml.Recommendation().Trainers.MatrixFactorization(new MatrixFactorizationTrainer.Options
                {
                    MatrixColumnIndexColumnName = "userIdEncoded",
                    MatrixRowIndexColumnName = "appIdEncoded",
                    LabelColumnName = nameof(InteractionTrain.Rating),
                    LossFunction = MatrixFactorizationTrainer.LossFunctionType.SquareLossOneClass,
                    Alpha = 0.01f,
                    Lambda = 0.025f,
                    NumberOfIterations = 30,
                    ApproximationRank = 64
                }));

            _model = pipeline.Fit(trainDv);
        }
        catch (Exception ex)
        {
            _model = null;
            LastError = ex.Message;
            throw;
        }
    }

    public List<(uint appId, float score)> RecommendForUser(
        string userId,
        IEnumerable<uint> candidateAppIds,
        ISet<uint> excludeAppIds,
        int topN = 20)
    {
        if (_model == null) throw new InvalidOperationException("Collaborative model not trained.");
        if (string.IsNullOrWhiteSpace(userId)) return new();

        if (!_knownUsers.Contains(userId))
            return new();

        var candidates = candidateAppIds
            .Where(a => _knownAppIds.Contains(a))
            .Where(a => !excludeAppIds.Contains(a))
            .Distinct()
            .ToList();

        if (candidates.Count == 0) return new();

        var batch = candidates.Select(a => new InteractionScoreInput
        {
            UserId = userId,
            AppId = a
        });

        var dv = _ml.Data.LoadFromEnumerable(batch);
        var scored = _model.Transform(dv);

        var preds = _ml.Data.CreateEnumerable<ScorePrediction>(scored, reuseRowObject: false).ToList();

        return candidates
            .Zip(preds, (appId, pred) => (appId, pred.Score))
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .ToList();
    }

    private class InteractionTrain
    {
        public string UserId { get; set; } = "";
        public uint AppId { get; set; }
        public float Rating { get; set; }
    }

    private class InteractionScoreInput
    {
        public string UserId { get; set; } = "";
        public uint AppId { get; set; }
    }

    private class ScorePrediction
    {
        public float Score { get; set; }
    }

    private static float ComputeImplicitRating(float playtimeForeverMinutes, float playtime2WeeksMinutes)
    {
        // ✅ More playtime => higher rating
        // ✅ log1p => diminishing returns, prevents extreme playtime from dominating
        float a = (float)Math.Log(1.0 + Math.Max(0.0, playtimeForeverMinutes));
        float b = (float)Math.Log(1.0 + Math.Max(0.0, playtime2WeeksMinutes));
        return a + RECENT_BONUS * b;
    }
}
