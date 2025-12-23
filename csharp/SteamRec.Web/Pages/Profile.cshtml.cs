using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamRec.Core;
using SteamRec.ML;
using SteamRec.Web.Services;

namespace SteamRec.Web.Pages;

public class ProfileModel : PageModel
{
    private readonly ContentBasedRecommender _recommender;
    private readonly SteamProfileService _profileService;
    private readonly CollaborativeFilteringRecommender _cf;
    private readonly InteractionRepository _interactionRepo;
    private readonly IReadOnlyList<GameRecord> _games;

    public ProfileModel(
        ContentBasedRecommender recommender,
        SteamProfileService profileService,
        CollaborativeFilteringRecommender cf,
        InteractionRepository interactionRepo)
    {
        _recommender = recommender;
        _profileService = profileService;
        _cf = cf;
        _interactionRepo = interactionRepo;
        _games = recommender.Games;
    }

    [BindProperty] public string? SteamId { get; set; }

    // "content" or "collab"
    [BindProperty] public string Algorithm { get; set; } = "content";

    // Opt-in
    [BindProperty] public bool ContributeToCollaborative { get; set; } = true;

    public bool CollaborativeAvailable => _cf.IsReady;
    public int TotalGames => _recommender.GameCount;

    public List<OwnedGameViewModel> MatchedOwnedGames { get; private set; } = new();
    public List<RecommendationViewModel> Recommendations { get; private set; } = new();
    public List<string> RadarLabels { get; private set; } = new();
    public List<double> UserRadarValues { get; private set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(SteamId))
            return Page();

        var steamId = SteamId.Trim();

        // 1) Fetch owned games from Steam
        var owned = await _profileService.GetOwnedGamesAsync(steamId);

        // 2) Store interactions if opted-in (Mongo ONLY)
        if (ContributeToCollaborative)
        {
            var meaningful = owned
                .Where(o => o.playtime_forever > 0 || o.playtime_2weeks > 0)
                .ToList();

            try
            {
                var docs = meaningful.Select(o => new InteractionDocument
                {
                    SteamId = steamId,
                    AppId = o.appid,
                    PlaytimeForever = o.playtime_forever,
                    Playtime2Weeks = o.playtime_2weeks,
                    UpdatedUtc = DateTime.UtcNow
                });

                await _interactionRepo.UpsertManyAsync(steamId, docs);
            }
            catch (Exception ex)
            {
                // We don't fallback to CSV anymore, so show a friendly error
                ModelState.AddModelError(string.Empty, "Could not save interactions to MongoDB: " + ex.Message);
            }
        }

        // 3) Intersect with our dataset for display + content-based liked list
        var ownedById = owned.ToDictionary(o => o.appid, o => o.playtime_forever);

        var matched = _games
            .Where(g => ownedById.ContainsKey(g.AppId))
            .Select(g => new OwnedGameViewModel
            {
                AppId = g.AppId,
                Name = g.Name,
                PlaytimeMinutes = ownedById[g.AppId]
            })
            .OrderByDescending(x => x.PlaytimeMinutes)
            .ToList();

        MatchedOwnedGames = matched;

        // liked = >= 60 min; fallback = top 10 by playtime
        var likedAppIds = matched
            .Where(m => m.PlaytimeMinutes >= 60)
            .Select(m => m.AppId)
            .ToList();

        if (likedAppIds.Count == 0 && matched.Count > 0)
        {
            likedAppIds = matched
                .OrderByDescending(m => m.PlaytimeMinutes)
                .Take(10)
                .Select(m => m.AppId)
                .ToList();
        }

        if (likedAppIds.Count == 0)
            return Page();

        BuildRadarProfile(likedAppIds);

        // 4) Recommend
        if (Algorithm == "collab" && _cf.IsReady)
        {
            var ownedSet = matched.Select(x => (uint)x.AppId).ToHashSet();
            var candidateAppIds = _games.Select(g => (uint)g.AppId);

            var scored = _cf.RecommendForUser(
                userId: steamId,
                candidateAppIds: candidateAppIds,
                excludeAppIds: ownedSet,
                topN: 20);

            var byId = _games.ToDictionary(g => (uint)g.AppId, g => g);

            Recommendations = scored
                .Where(s => byId.ContainsKey(s.appId))
                .Select(s =>
                {
                    var game = byId[s.appId];
                    return new RecommendationViewModel
                    {
                        AppId = (int)s.appId,
                        Name = game.Name,
                        Similarity = s.score,
                        OverallScore = s.score,
                        ReviewTotal = game.ReviewTotal,
                        ReviewScoreAdj = game.ReviewScoreAdj,
                        PriceEur = game.PriceEur,
                        MetacriticScore = game.MetacriticScore,
                        ReleaseYear = game.ReleaseYear,
                        RequiredAge = game.RequiredAge,
                        GameRadarValues = BuildRadarVector(game)
                    };
                })
                .ToList();
        }
        else
        {
            var recs = _recommender.RecommendForLiked(likedAppIds, topN: 20);
            Recommendations = recs
                .Select(r => new RecommendationViewModel
                {
                    AppId = r.game.AppId,
                    Name = r.game.Name,
                    Similarity = r.similarity,
                    OverallScore = r.overallScore,
                        ReviewTotal = r.game.ReviewTotal,
                        ReviewScoreAdj = r.game.ReviewScoreAdj,
                        PriceEur = r.game.PriceEur,
                        MetacriticScore = r.game.MetacriticScore,
                        ReleaseYear = r.game.ReleaseYear,
                        RequiredAge = r.game.RequiredAge,
                        GameRadarValues = BuildRadarVector(r.game)
                    })
                    .ToList();
        }

        return Page();
    }

    public class OwnedGameViewModel
    {
        public int AppId { get; set; }
        public string Name { get; set; } = "";
        public int PlaytimeMinutes { get; set; }
        public double PlaytimeHours => PlaytimeMinutes / 60.0;
    }

    public class RecommendationViewModel
    {
        public int AppId { get; set; }
        public string Name { get; set; } = "";
        public double Similarity { get; set; }
        public double OverallScore { get; set; }
        public int ReviewTotal { get; set; }
        public double ReviewScoreAdj { get; set; }
        public double PriceEur { get; set; }
        public double MetacriticScore { get; set; }
        public int ReleaseYear { get; set; }
        public int RequiredAge { get; set; }
        public List<double> GameRadarValues { get; set; } = new();
    }

    private void BuildRadarProfile(List<int> likedAppIds)
    {
        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var appId in likedAppIds)
        {
            var game = _games.FirstOrDefault(g => g.AppId == appId);
            if (game == null) continue;

            foreach (var tag in game.Tags) Increment(freq, tag);
            foreach (var genre in game.Genres) Increment(freq, genre);
        }

        RadarLabels = freq
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(kv => kv.Key)
            .ToList();

        var max = freq.Values.DefaultIfEmpty(1).Max();
        UserRadarValues = RadarLabels
            .Select(label => freq.TryGetValue(label, out var count) ? count / (double)max : 0.0)
            .ToList();
    }

    private static void Increment(Dictionary<string, int> freq, string key)
    {
        if (!freq.ContainsKey(key))
            freq[key] = 0;
        freq[key]++;
    }

    private List<double> BuildRadarVector(GameRecord game)
    {
        if (RadarLabels.Count == 0) return new();

        bool ContainsLabel(GameRecord g, string label)
            => g.Tags.Contains(label) || g.Genres.Contains(label) || g.Categories.Contains(label);

        return RadarLabels
            .Select(label => ContainsLabel(game, label) ? 1.0 : 0.0)
            .ToList();
    }

    public string SerializeRadarLabels() => string.Join("|", RadarLabels);

    public string SerializeValues(IEnumerable<double> values) =>
        string.Join(",", values.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)));
}
