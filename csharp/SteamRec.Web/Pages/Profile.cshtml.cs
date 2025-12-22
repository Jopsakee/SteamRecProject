using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamRec.Core;
using System.Text.Json;
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

        var likedPreferences = likedAppIds
            .Select(id =>
            {
                var match = matched.First(m => m.AppId == id);
                return new ContentBasedRecommender.LikedGamePreference
                {
                    AppId = id,
                    Weight = match.PlaytimeMinutes
                };
            })
            .ToList();

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
                        ReviewScoreAdj = game.ReviewScoreAdj
                    };
                })
                .ToList();
        }
        else
        {
            var recs = _recommender.RecommendForLikedWithInsights(likedPreferences, topN: 20);
            Recommendations = recs
                .Select(r => new RecommendationViewModel
                {
                    AppId = r.Game.AppId,
                    Name = r.Game.Name,
                    Similarity = r.Similarity,
                    OverallScore = r.OverallScore,
                    ReviewTotal = r.Game.ReviewTotal,
                    ReviewScoreAdj = r.Game.ReviewScoreAdj,
                    Axes = r.Axes.Select(a => new AxisViewModel
                    {
                        Name = a.Name,
                        Source = a.Source,
                        UserScore = a.UserScore,
                        GameScore = a.GameScore
                    }).ToList(),
                    Influencers = r.Influencers.Select(i => new InfluenceViewModel
                    {
                        AppId = i.AppId,
                        Name = i.Name,
                        Similarity = i.Similarity,
                        InfluenceScore = i.InfluenceScore,
                        PlaytimeHours = i.Weight / 60.0
                    }).ToList()
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
        public List<AxisViewModel> Axes { get; set; } = new();
        public List<InfluenceViewModel> Influencers { get; set; } = new();
        public string AxesJson => JsonSerializer.Serialize(
            Axes,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    public class AxisViewModel
    {
        public string Name { get; set; } = "";
        public string Source { get; set; } = "";
        public double UserScore { get; set; }
        public double GameScore { get; set; }
    }

    public class InfluenceViewModel
    {
        public int AppId { get; set; }
        public string Name { get; set; } = "";
        public double Similarity { get; set; }
        public double InfluenceScore { get; set; }
        public double PlaytimeHours { get; set; }
    }
}
