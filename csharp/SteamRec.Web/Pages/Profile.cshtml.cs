using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamRec.Core;
using SteamRec.ML;
using SteamRec.Web.Services;
using static SteamRec.Web.Services.SteamProfileService;

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
    public bool ShowPrivacyGuide { get; private set; }

    public List<OwnedGameViewModel> MatchedOwnedGames { get; private set; } = new();
    public List<RecommendationViewModel> Recommendations { get; private set; } = new();

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(SteamId))
            return Page();

        var steamId = SteamId.Trim();

        // 1) Fetch owned games from Steam
        List<SteamProfileService.OwnedGame> owned;
        try
        {
            owned = await _profileService.GetOwnedGamesAsync(steamId);
        }
        catch (PrivateProfileException pex)
        {
            ModelState.AddModelError(nameof(SteamId), pex.Message);
            ShowPrivacyGuide = true;
            return Page();
        }

        // 2) Store interactions if opted-in
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
                PlaytimeMinutes = ownedById[g.AppId],
                ThumbnailUrl = SteamImageHelper.BuildCapsuleUrl(g.AppId),
                StoreUrl = SteamImageHelper.BuildStorePageUrl(g.AppId)
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
                        ThumbnailUrl = SteamImageHelper.BuildCapsuleUrl((int)s.appId),
                        StoreUrl = SteamImageHelper.BuildStorePageUrl((int)s.appId)
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
                    ThumbnailUrl = SteamImageHelper.BuildCapsuleUrl(r.game.AppId)
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
        public string ThumbnailUrl { get; set; } = "";
        public string StoreUrl { get; set; } = "";
    }

    public class RecommendationViewModel
    {
        public int AppId { get; set; }
        public string Name { get; set; } = "";
        public double Similarity { get; set; }
        public double OverallScore { get; set; }
        public int ReviewTotal { get; set; }
        public double ReviewScoreAdj { get; set; }
        public string ThumbnailUrl { get; set; } = "";
        public string StoreUrl { get; set; } = "";
    }
}
