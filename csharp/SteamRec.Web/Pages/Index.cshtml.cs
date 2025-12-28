using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SteamRec.Core;
using SteamRec.Web.Services;

namespace SteamRec.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IRecommenderProvider _recommenderProvider;
    private IReadOnlyList<GameRecord> _games = Array.Empty<GameRecord>();

    public IndexModel(IRecommenderProvider recommenderProvider)
    {
        _recommenderProvider = recommenderProvider;
    }

    [BindProperty]
    public string? SearchTerm { get; set; }

    [BindProperty]
    public int SelectedAppId { get; set; }

    public int TotalGames { get; private set; }

    public List<GameRecord> SearchResults { get; private set; } = new();
    public List<RecommendationViewModel> Recommendations { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var recommender = await _recommenderProvider.GetContentBasedAsync();
        _games = recommender.Games;
        TotalGames = recommender.GameCount;
    }

    public async Task<IActionResult> OnPostSearchAsync()
    {
        var recommender = await _recommenderProvider.GetContentBasedAsync();
        _games = recommender.Games;
        TotalGames = recommender.GameCount;

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            SearchResults = _games
                .Where(g => g.Name.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase))
                .OrderBy(g => g.Name)
                .Take(25)
                .ToList();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRecommendAsync()
    {
        // rebuild search results so dropdown stays populated
        await OnPostSearchAsync();

        if (SelectedAppId <= 0) return Page();

        var recommender = await _recommenderProvider.GetContentBasedAsync();
        _games = recommender.Games;
        TotalGames = recommender.GameCount;
        var recs = recommender.RecommendSimilar(SelectedAppId, topN: 10);

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
                ThumbnailUrl = SteamImageHelper.BuildCapsuleUrl(r.game.AppId),
                StoreUrl = SteamImageHelper.BuildStorePageUrl(r.game.AppId)
            })
            .ToList();

        return Page();
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
        public string ThumbnailUrl { get; set; } = "";
        public string StoreUrl { get; set; } = "";
    }
}
