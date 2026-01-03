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
    private const int RecommendationsPerPage = 10;
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
    public int CurrentPage { get; private set; } = 1;
    public bool HasNextPage { get; private set; }
    public bool HasPreviousPage { get; private set; }

    public List<GameRecord> SearchResults { get; private set; } = new();
    public List<RecommendationViewModel> Recommendations { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await LoadGamesAsync();
    }

    public async Task<IActionResult> OnPostSearchAsync()
    {
        await LoadGamesAsync();
        BuildSearchResults();

        return Page();
    }

    public async Task<IActionResult> OnPostRecommendAsync()
    {
        await LoadGamesAsync();
        BuildSearchResults();

        if (SelectedAppId <= 0)
        {
            return Page();
        }

        await LoadRecommendationsAsync(1);

        return Page();
    }

    public async Task<IActionResult> OnPostRecommendPageAsync(int pageNumber)
    {
        await LoadGamesAsync();
        BuildSearchResults();

        if (SelectedAppId <= 0)
        {
            return Page();
        }

        await LoadRecommendationsAsync(pageNumber);

        return Page();
    }

    private async Task LoadGamesAsync()
    {
        var recommender = await _recommenderProvider.GetContentBasedAsync();
        _games = recommender.Games;
        TotalGames = recommender.GameCount;
    }

     private void BuildSearchResults()
    {
        var term = SearchTerm?.Trim();
        if (string.IsNullOrWhiteSpace(term))
            return;

        SearchResults = _games
            .Where(g => g.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.Name.Equals(term, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(g => g.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(g => g.Name)
            .Take(50)
            .ToList();
    }
    private async Task LoadRecommendationsAsync(int pageNumber)
    {
        var safePage = Math.Max(1, pageNumber);

        var recommender = await _recommenderProvider.GetContentBasedAsync();
        var neededCount = safePage * RecommendationsPerPage + 1;
        var recs = recommender.RecommendSimilar(SelectedAppId, topN: neededCount).ToList();

        HasNextPage = recs.Count > safePage * RecommendationsPerPage;
        HasPreviousPage = safePage > 1;
        CurrentPage = safePage;

        Recommendations = recs
            .Skip((safePage - 1) * RecommendationsPerPage)
            .Take(RecommendationsPerPage)
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
