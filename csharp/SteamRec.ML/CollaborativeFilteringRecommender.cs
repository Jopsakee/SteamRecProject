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

    // What the model has actually seen
    private HashSet<uint> _knownAppIds = new();
    private HashSet<string> _knownUsers = new(StringComparer.Ordinal);

    // How we convert playtime -> implicit "rating"
    // rating = log(1+forever_minutes) + BONUS * log(1+minutes_2weeks)
    private const float RECENT_BONUS = 0.35f;

    public void TrainFromCsv(string interactionsCsvPath)
    {
        try
        {
            LastError = null;

            if (!File.Exists(interactionsCsvPath))
                throw new FileNotFoundException("interactions.csv not found", interactionsCsvPath);

            // Load raw interactions (your schema)
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

            // Build training rows with computed Rating (implicit feedback strength)
            var trainRows = raw.Select(r => new InteractionTrain
            {
                UserId = r.SteamId.Trim(),
                AppId = r.AppId,
                Rating = ComputeImplicitRating(r.PlaytimeForever, r.Playtime2Weeks)
            })
            // remove zero-signal rows if you want (optional)
            .Where(x => x.Rating > 0f)
            .ToList();

            _knownAppIds = trainRows.Select(x => x.AppId).ToHashSet();
            _knownUsers = trainRows.Select(x => x.UserId).ToHashSet(StringComparer.Ordinal);

            if (_knownUsers.Count < 2 || _knownAppIds.Count < 50)
                throw new InvalidOperationException(
                    $"Not enough interaction data for CF. Users={_knownUsers.Count}, Items={_knownAppIds.Count}. " +
                    $"Add more SteamIDs to interactions.csv.");

            var trainDv = _ml.Data.LoadFromEnumerable(trainRows);

            // Pipeline: Key-encode user & item, then Matrix Factorization
            var pipeline =
                _ml.Transforms.Conversion.MapValueToKey(outputColumnName: "userIdEncoded", inputColumnName: nameof(InteractionTrain.UserId))
                .Append(_ml.Transforms.Conversion.MapValueToKey(outputColumnName: "appIdEncoded", inputColumnName: nameof(InteractionTrain.AppId)))
                .Append(_ml.Recommendation().Trainers.MatrixFactorization(new MatrixFactorizationTrainer.Options
                {
                    MatrixColumnIndexColumnName = "userIdEncoded",
                    MatrixRowIndexColumnName = "appIdEncoded",
                    LabelColumnName = nameof(InteractionTrain.Rating),

                    // Implicit feedback setup
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

        // If user never appeared in training, MF can’t personalize
        if (!_knownUsers.Contains(userId))
            return new();

        var candidates = candidateAppIds
            .Where(a => _knownAppIds.Contains(a))
            .Where(a => !excludeAppIds.Contains(a))
            .Distinct()
            .ToList();

        if (candidates.Count == 0) return new();

        // Batch-score
        var batch = candidates.Select(a => new InteractionScoreInput
        {
            UserId = userId,
            AppId = a
        });

        var dv = _ml.Data.LoadFromEnumerable(batch);
        var scored = _model.Transform(dv);

        var preds = _ml.Data.CreateEnumerable<ScorePrediction>(scored, reuseRowObject: false).ToList();

        var results = candidates
            .Zip(preds, (appId, pred) => (appId, pred.Score))
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .ToList();

        return results;
    }

    /// <summary>
    /// Offline evaluation: per-user holdout split, measure HitRate@K and Recall@K.
    /// Uses implicit "liked items" as those present in interactions.csv for that user.
    /// </summary>
    public (double hitRateAtK, double recallAtK, int usersEvaluated) EvaluateHitRateAtK(
        string interactionsCsvPath,
        int k = 20,
        double testFraction = 0.2,
        int minItemsPerUser = 10)
    {
        if (!File.Exists(interactionsCsvPath))
            return (0, 0, 0);

        var rawDv = _ml.Data.LoadFromTextFile<InteractionRaw>(interactionsCsvPath, hasHeader: true, separatorChar: ',');
        var raw = _ml.Data.CreateEnumerable<InteractionRaw>(rawDv, reuseRowObject: false)
            .Where(r => !string.IsNullOrWhiteSpace(r.SteamId))
            .Where(r => r.AppId > 0)
            .ToList();

        var grouped = raw
            .GroupBy(r => r.SteamId.Trim())
            .Select(g => new
            {
                UserId = g.Key,
                AppIds = g.Select(x => x.AppId).Distinct().ToList(),
                Rows = g.ToList()
            })
            .Where(x => x.AppIds.Count >= minItemsPerUser)
            .ToList();

        if (grouped.Count == 0)
            return (0, 0, 0);

        var rng = new Random(42);

        // Split into train/test per user
        var trainRows = new List<InteractionTrain>();
        var testByUser = new Dictionary<string, HashSet<uint>>(StringComparer.Ordinal);

        foreach (var u in grouped)
        {
            var shuffled = u.AppIds.OrderBy(_ => rng.Next()).ToList();
            int testCount = Math.Max(1, (int)Math.Round(shuffled.Count * testFraction));

            var testItems = shuffled.Take(testCount).ToHashSet();
            var trainItems = shuffled.Skip(testCount).ToHashSet();

            testByUser[u.UserId] = testItems;

            foreach (var r in u.Rows.Where(r => trainItems.Contains(r.AppId)))
            {
                trainRows.Add(new InteractionTrain
                {
                    UserId = u.UserId,
                    AppId = r.AppId,
                    Rating = ComputeImplicitRating(r.PlaytimeForever, r.Playtime2Weeks)
                });
            }
        }

        // Train a temporary model on the split
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

        var model = pipeline.Fit(trainDv);

        var trainedAppIds = trainRows.Select(x => x.AppId).Distinct().ToList();

        int users = 0;
        int hitUsers = 0;
        double recallSum = 0.0;

        foreach (var (userId, testItems) in testByUser)
        {
            var ownedTrain = trainRows.Where(x => x.UserId == userId).Select(x => x.AppId).ToHashSet();
            if (ownedTrain.Count == 0) continue;

            var candidateIds = trainedAppIds.Where(a => !ownedTrain.Contains(a)).ToList();
            if (candidateIds.Count == 0) continue;

            var batch = candidateIds.Select(a => new InteractionScoreInput { UserId = userId, AppId = a });
            var dv = _ml.Data.LoadFromEnumerable(batch);
            var scored = model.Transform(dv);
            var preds = _ml.Data.CreateEnumerable<ScorePrediction>(scored, reuseRowObject: false).ToList();

            var topK = candidateIds
                .Zip(preds, (appId, pred) => (appId, pred.Score))
                .OrderByDescending(x => x.Score)
                .Take(k)
                .Select(x => x.appId)
                .ToHashSet();

            int hits = topK.Count(a => testItems.Contains(a));
            if (hits > 0) hitUsers++;

            recallSum += (double)hits / testItems.Count;
            users++;
        }

        if (users == 0) return (0, 0, 0);

        return ((double)hitUsers / users, recallSum / users, users);
    }

    // ----------------- Internal types -----------------

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
        // log1p transform (prevents huge playtime dominating too hard)
        float a = (float)Math.Log(1.0 + Math.Max(0.0, playtimeForeverMinutes));
        float b = (float)Math.Log(1.0 + Math.Max(0.0, playtime2WeeksMinutes));
        return a + RECENT_BONUS * b;
    }
}
