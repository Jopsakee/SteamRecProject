using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Trainers;

namespace SteamRec.ML;

public class CollaborativeFilteringRecommender
{
    private readonly MLContext _ml = new(seed: 42);
    private ITransformer? _model;
    private readonly object _trainLock = new();
    public bool IsReady => _model != null;
    public string? LastError { get; private set; }

    private HashSet<uint> _knownAppIds = new();
    private HashSet<string> _knownUsers = new(StringComparer.Ordinal);

    // rating = log(1+forever_minutes) + BONUS * log(1+minutes_2weeks)
    private const float RECENT_BONUS = 0.35f;

    public void TrainFromRows(IEnumerable<(string steamId, uint appId, float playtimeForever, float playtime2Weeks)> rows)
    {
        lock (_trainLock)
        {
            try
            {
                LastError = null;

                // Clean the raw input before we feed it to ML.NET.
                var raw = rows
                    .Where(r => !string.IsNullOrWhiteSpace(r.steamId))
                    .Where(r => r.appId > 0)
                    // Ignore zero interactions (0 minutes should not count as positive signal)
                    .Where(r => (r.playtimeForever > 0f) || (r.playtime2Weeks > 0f))
                    .ToList();

                if (raw.Count == 0)
                    throw new InvalidOperationException("No valid interaction rows to train on (all were empty/zero).");

                var trainRows = raw.Select(r => new InteractionTrain
                {
                    UserId = r.steamId.Trim(),
                    AppId = r.appId,
                    Rating = ComputeImplicitRating(r.playtimeForever, r.playtime2Weeks)
                })
                .Where(x => x.Rating > 0f)
                .ToList();

                _knownAppIds = trainRows.Select(x => x.AppId).ToHashSet();
                _knownUsers = trainRows.Select(x => x.UserId).ToHashSet(StringComparer.Ordinal);

                if (_knownUsers.Count < 2 || _knownAppIds.Count < 50)
                    throw new InvalidOperationException(
                        $"Not enough interaction data for CF. Users={_knownUsers.Count}, Items={_knownAppIds.Count}. Add more SteamIDs.");

                var trainDv = _ml.Data.LoadFromEnumerable(trainRows);

                // Matrix factorization for implicit feedback (playtime as signal).
                var pipeline =
                        _ml.Transforms.Conversion.MapValueToKey("userIdEncoded", nameof(InteractionTrain.UserId))
                        .Append(_ml.Transforms.Conversion.MapValueToKey("appIdEncoded", nameof(InteractionTrain.AppId)))
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
    }


    public List<(uint appId, float score)> RecommendForUser(
        string userId,
        IEnumerable<uint> candidateAppIds,
        ISet<uint> excludeAppIds,
        int topN = 20)
    {
        ITransformer? model;
        HashSet<string> knownUsers;
        HashSet<uint> knownAppIds;

        // Snapshot the model and ID sets so training can't change them mid-request.
        lock (_trainLock)
        {
            model = _model;
            knownUsers = new HashSet<string>(_knownUsers, StringComparer.Ordinal);
            knownAppIds = new HashSet<uint>(_knownAppIds);
        }

        if (model == null) throw new InvalidOperationException("Collaborative model not trained.");
        if (string.IsNullOrWhiteSpace(userId)) return new();

        // If user never appeared in training, MF can’t personalize
        if (!knownUsers.Contains(userId))
            return new();

        var candidates = candidateAppIds
            .Where(a => knownAppIds.Contains(a))
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
        var scored = model.Transform(dv);

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
        float a = (float)Math.Log(1.0 + Math.Max(0.0, playtimeForeverMinutes));
        float b = (float)Math.Log(1.0 + Math.Max(0.0, playtime2WeeksMinutes));
        return a + RECENT_BONUS * b;
    }
}
