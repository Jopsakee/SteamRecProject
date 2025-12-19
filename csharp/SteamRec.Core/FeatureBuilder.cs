using System;
using System.Collections.Generic;
using System.Linq;

namespace SteamRec.Core;

public class FeatureBuilder
{
    public List<string> GenreVocab { get; private set; } = new();
    public List<string> TagVocab { get; private set; } = new();
    public List<string> CategoryVocab { get; private set; } = new();

    private readonly string[] _numericNames =
    {
        "price_eur",
        "metacritic_score",
        "release_year",
        "required_age",
        "is_free",
        "review_score_adj",
        "review_volume_log"
    };

    private Dictionary<string, double> _means = new();
    private Dictionary<string, double> _stds = new();

    private const double REVIEW_SCORE_INTERNAL_WEIGHT = 2.0;
    private const double REVIEW_VOL_INTERNAL_WEIGHT = 1.2;

    private const double GENRE_BLOCK_WEIGHT = 1.8;
    private const double TAG_BLOCK_WEIGHT = 2.8;
    private const double CAT_BLOCK_WEIGHT = 1.2;

    public void BuildFeatures(List<GameRecord> games)
    {
        if (games == null || games.Count == 0)
            throw new InvalidOperationException("No games loaded in FeatureBuilder.BuildFeatures. Check CSV path and GameDataLoader.");

        // 1) Build vocabularies
        var genreSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var catSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in games)
        {
            foreach (var ge in g.Genres) genreSet.Add(ge);
            foreach (var t in g.Tags) tagSet.Add(t);
            foreach (var c in g.Categories) catSet.Add(c);
        }

        GenreVocab = genreSet.OrderBy(x => x).ToList();
        TagVocab = tagSet.OrderBy(x => x).ToList();
        CategoryVocab = catSet.OrderBy(x => x).ToList();

        // 2) Compute numeric mean/std
        var numericValues = _numericNames.ToDictionary(
            name => name,
            _ => new List<double>()
        );

        foreach (var g in games)
        {
            numericValues["price_eur"].Add(g.PriceEur);
            numericValues["metacritic_score"].Add(g.MetacriticScore);
            numericValues["release_year"].Add(g.ReleaseYear);
            numericValues["required_age"].Add(g.RequiredAge);
            numericValues["is_free"].Add(g.IsFree ? 1.0 : 0.0);
            numericValues["review_score_adj"].Add(g.ReviewScoreAdj);
            numericValues["review_volume_log"].Add(g.ReviewVolumeLog);
        }

        _means = numericValues.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Average()
        );

        _stds = numericValues.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var vals = kv.Value;
                var mean = _means[kv.Key];
                var variance = vals.Count > 1
                    ? vals.Sum(v => (v - mean) * (v - mean)) / (vals.Count - 1)
                    : 1.0;
                var std = Math.Sqrt(variance);
                return std == 0 ? 1.0 : std;
            });

        // 3) Build feature vector for each game
        foreach (var g in games)
        {
            // numeric z-scores with internal weights
            var numeric = new double[_numericNames.Length];
            for (int i = 0; i < _numericNames.Length; i++)
            {
                var name = _numericNames[i];

                double raw = name switch
                {
                    "price_eur" => g.PriceEur,
                    "metacritic_score" => g.MetacriticScore,
                    "release_year" => g.ReleaseYear,
                    "required_age" => g.RequiredAge,
                    "is_free" => g.IsFree ? 1.0 : 0.0,
                    "review_score_adj" => g.ReviewScoreAdj,
                    "review_volume_log" => g.ReviewVolumeLog,
                    _ => 0.0
                };

                double mean = _means[name];
                double std = _stds[name];
                double z = (raw - mean) / std;

                if (name == "review_score_adj")
                    z *= REVIEW_SCORE_INTERNAL_WEIGHT;
                if (name == "review_volume_log")
                    z *= REVIEW_VOL_INTERNAL_WEIGHT;

                numeric[i] = z;
            }

            // genres one-hot
            var genreVec = new double[GenreVocab.Count];
            for (int i = 0; i < GenreVocab.Count; i++)
            {
                if (g.Genres.Contains(GenreVocab[i]))
                    genreVec[i] = GENRE_BLOCK_WEIGHT;
            }

            // tags one-hot
            var tagVec = new double[TagVocab.Count];
            for (int i = 0; i < TagVocab.Count; i++)
            {
                if (g.Tags.Contains(TagVocab[i]))
                    tagVec[i] = TAG_BLOCK_WEIGHT;
            }

            // categories one-hot
            var catVec = new double[CategoryVocab.Count];
            for (int i = 0; i < CategoryVocab.Count; i++)
            {
                if (g.Categories.Contains(CategoryVocab[i]))
                    catVec[i] = CAT_BLOCK_WEIGHT;
            }

            var features = new double[numeric.Length + genreVec.Length + tagVec.Length + catVec.Length];
            Array.Copy(numeric, 0, features, 0, numeric.Length);
            Array.Copy(genreVec, 0, features, numeric.Length, genreVec.Length);
            Array.Copy(tagVec, 0, features, numeric.Length + genreVec.Length, tagVec.Length);
            Array.Copy(catVec, 0, features, numeric.Length + genreVec.Length + tagVec.Length, catVec.Length);

            g.Features = features;
        }
    }
}
