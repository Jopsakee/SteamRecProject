using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamRec.Core;

namespace SteamRec.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ContentBasedRecommender _recommender;
    private readonly IReadOnlyList<GameRecord> _games;

    public IndexModel(ContentBasedRecommender recommender)
    {
        _recommender = recommender;
        _games = recommender.Games;
    }

    [BindProperty]
    public string? SearchTerm { get; set; }

    [BindProperty]
    public int SelectedAppId { get; set; }

    public int TotalGames => _recommender.GameCount;

    public List<GameRecord> SearchResults { get; private set; } = new();
    public List<RecommendationViewModel> Recommendations { get; private set; } = new();

    public void OnGet()
    {
    }

    public void OnPostSearch()
    {
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            SearchResults = _games
                .Where(g => g.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
                .OrderBy(g => g.Name)
                .Take(25)
                .ToList();
        }
    }

    public void OnPostRecommend()
    {
        // rebuild search results so dropdown stays populated
        OnPostSearch();

        if (SelectedAppId <= 0) return;

        var recs = _recommender.RecommendSimilar(SelectedAppId, topN: 10);

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
                RequiredAge = r.game.RequiredAge
            })
            .ToList();
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
    }
}
