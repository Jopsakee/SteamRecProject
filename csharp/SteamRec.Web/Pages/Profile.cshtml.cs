using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamRec.Core;
using SteamRec.Web.Services;

namespace SteamRec.Web.Pages;

public class ProfileModel : PageModel
{
    private readonly ContentBasedRecommender _recommender;
    private readonly SteamProfileService _profileService;
    private readonly IReadOnlyList<GameRecord> _games;

    public ProfileModel(ContentBasedRecommender recommender, SteamProfileService profileService)
    {
        _recommender = recommender;
        _profileService = profileService;
        _games = recommender.Games;
    }

    [BindProperty]
    public string? SteamId { get; set; }

    public int TotalGames => _recommender.GameCount;

    public List<OwnedGameViewModel> MatchedOwnedGames { get; private set; } = new();
    public List<RecommendationViewModel> Recommendations { get; private set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(SteamId))
            return Page();

        // 1) Fetch owned games from Steam
        var owned = await _profileService.GetOwnedGamesAsync(SteamId.Trim());

        // 2) Intersect with our dataset
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

        // 3) Choose liked appids (e.g. at least 60 min playtime)
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

        if (likedAppIds.Count > 0)
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
                    ReviewScoreAdj = r.game.ReviewScoreAdj
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
    }
}
