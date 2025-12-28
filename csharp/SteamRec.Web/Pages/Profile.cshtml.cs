using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using SteamRec.Core;
using SteamRec.ML;
using SteamRec.Web.Services;
using static SteamRec.Web.Services.SteamProfileService;

namespace SteamRec.Web.Pages;

public class ProfileModel : PageModel
{
    private const int RecommendationsPerPage = 10;
    private const int RecommendationsCacheSize = 100;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private readonly IRecommenderProvider _recommenderProvider;
    private readonly SteamProfileService _profileService;
    private readonly CollaborativeFilteringRecommender _cf;
    private readonly InteractionRepository _interactionRepo;
    private readonly IMemoryCache _cache;
    private IReadOnlyList<GameRecord> _games = Array.Empty<GameRecord>();

    public ProfileModel(
        IRecommenderProvider recommenderProvider,
        SteamProfileService profileService,
        CollaborativeFilteringRecommender cf,
        InteractionRepository interactionRepo,
        IMemoryCache cache)
    {
        _recommenderProvider = recommenderProvider;
        _profileService = profileService;
        _cf = cf;
        _interactionRepo = interactionRepo;
        _cache = cache;
    }

    [BindProperty] public string? SteamId { get; set; }

    // "content" or "collab"
    [BindProperty] public string Algorithm { get; set; } = "content";

    // Opt-in
    [BindProperty] public bool ContributeToCollaborative { get; set; } = true;

    public bool CollaborativeAvailable => _cf.IsReady;
    public int TotalGames { get; private set; }
    public bool ShowPrivacyGuide { get; private set; }
    public int CurrentPage { get; private set; } = 1;
    public bool HasNextPage { get; private set; }
    public bool HasPreviousPage { get; private set; }

    public List<OwnedGameViewModel> MatchedOwnedGames { get; private set; } = new();
    public List<RecommendationViewModel> Recommendations { get; private set; } = new();
    public List<string> RadarLabels { get; private set; } = new();
    public List<double> UserRadarValues { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await LoadGamesAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        return await HandleRequestAsync(1);
    }

    public async Task<IActionResult> OnPostPageAsync(int pageNumber)
    {
        if (string.IsNullOrWhiteSpace(SteamId))
            return Page();

        await LoadGamesAsync();

        if (TryRestoreFromCache(pageNumber))
        {
            return Page();
        }

        return await HandleRequestAsync(pageNumber);
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
        public double PriceEur { get; set; }
        public double MetacriticScore { get; set; }
        public int ReleaseYear { get; set; }
        public int RequiredAge { get; set; }
        public List<double> GameRadarValues { get; set; } = new();
        public string ThumbnailUrl { get; set; } = "";
        public string StoreUrl { get; set; } = "";
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

    private async Task<ContentBasedRecommender> LoadGamesAsync()
    {
        var recommender = await _recommenderProvider.GetContentBasedAsync();
        _games = recommender.Games;
        TotalGames = recommender.GameCount;
        return recommender;
    }

    private async Task<IActionResult> HandleRequestAsync(int pageNumber)
    {
        if (string.IsNullOrWhiteSpace(SteamId))
            return Page();

        var recommender = await LoadGamesAsync();

        var steamInput = SteamId.Trim();
        string steamId;
        try
        {
            steamId = await _profileService.ResolveSteamIdAsync(steamInput);
            SteamId = steamId;
        }
        catch (InvalidSteamIdException iex)
        {
            ModelState.AddModelError(nameof(SteamId), iex.Message);
            return Page();
        }
        
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

        BuildRadarProfile(likedAppIds);
        var allRecommendations = LoadRecommendations(recommender, likedAppIds, matched, steamId, pageNumber);
        StoreInCache(steamId, allRecommendations);
        ApplyRecommendationPage(allRecommendations, pageNumber);

        return Page();
    }

    private List<RecommendationViewModel> LoadRecommendations(
        ContentBasedRecommender recommender,
        List<int> likedAppIds,
        List<OwnedGameViewModel> matched,
        string steamId,
        int pageNumber)
    {
        var safePage = Math.Max(1, pageNumber);
        var neededCount = safePage * RecommendationsPerPage + 1;
        var requestCount = Math.Max(neededCount, RecommendationsCacheSize);

        List<RecommendationViewModel> allRecommendations;

        if (Algorithm == "collab" && _cf.IsReady)
        {
            var ownedSet = matched.Select(x => (uint)x.AppId).ToHashSet();
            var candidateAppIds = _games.Select(g => (uint)g.AppId);

            var scored = _cf.RecommendForUser(
                userId: steamId,
                candidateAppIds: candidateAppIds,
                excludeAppIds: ownedSet,
                topN: requestCount);

            var byId = _games.ToDictionary(g => (uint)g.AppId, g => g);

            allRecommendations = scored
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
                        GameRadarValues = BuildRadarVector(game),
                        ThumbnailUrl = SteamImageHelper.BuildCapsuleUrl((int)s.appId),
                        StoreUrl = SteamImageHelper.BuildStorePageUrl((int)s.appId)
                    };
                })
                .ToList();
        }
        else
        {
            var recs = recommender.RecommendForLiked(likedAppIds, topN: requestCount);
            allRecommendations = recs
                .Select(r => new RecommendationViewModel
                {
                    AppId = r.game.AppId,
                    Name = r.game.Name,
                    Similarity = r.similarity,
                    OverallScore = r.overallScore,
                        ReviewTotal = r.game.ReviewTotal,
                    ReviewScoreAdj = r.game.ReviewScoreAdj,
                    ThumbnailUrl = SteamImageHelper.BuildCapsuleUrl(r.game.AppId),
                    PriceEur = r.game.PriceEur,
                    MetacriticScore = r.game.MetacriticScore,
                    ReleaseYear = r.game.ReleaseYear,
                    RequiredAge = r.game.RequiredAge,
                    GameRadarValues = BuildRadarVector(r.game)
                })
                .ToList();
        }
        return allRecommendations;
    }

    private void ApplyRecommendationPage(List<RecommendationViewModel> allRecommendations, int pageNumber)
    {
        var safePage = Math.Max(1, pageNumber);
        HasNextPage = allRecommendations.Count > safePage * RecommendationsPerPage;
        HasPreviousPage = safePage > 1;
        CurrentPage = safePage;
        Recommendations = allRecommendations
            .Skip((safePage - 1) * RecommendationsPerPage)
            .Take(RecommendationsPerPage)
            .ToList();
    }

    private bool TryRestoreFromCache(int pageNumber)
    {
        var steamId = SteamId?.Trim();
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return false;
        }

        var cacheKey = BuildCacheKey(steamId);
        if (!_cache.TryGetValue(cacheKey, out ProfileRecommendationCache? cached) || cached is null)
        {
            return false;
        }

        var requiredCount = Math.Max(1, pageNumber) * RecommendationsPerPage + 1;
        if (cached.Recommendations.Count < requiredCount)
        {
            return false;
        }

        MatchedOwnedGames = cached.MatchedOwnedGames;
        RadarLabels = cached.RadarLabels;
        UserRadarValues = cached.UserRadarValues;
        ApplyRecommendationPage(cached.Recommendations, pageNumber);
        return true;
    }

    private void StoreInCache(string steamId, List<RecommendationViewModel> recommendations)
    {
        var cacheKey = BuildCacheKey(steamId);
        var cached = new ProfileRecommendationCache
        {
            MatchedOwnedGames = MatchedOwnedGames,
            Recommendations = recommendations,
            RadarLabels = RadarLabels,
            UserRadarValues = UserRadarValues
        };

        _cache.Set(cacheKey, cached, CacheDuration);
    }

    private string BuildCacheKey(string steamId)
    {
        return $"profile-recs:{steamId}:{Algorithm}:{ContributeToCollaborative}";
    }

    private class ProfileRecommendationCache
    {
        public List<OwnedGameViewModel> MatchedOwnedGames { get; init; } = new();
        public List<RecommendationViewModel> Recommendations { get; init; } = new();
        public List<string> RadarLabels { get; init; } = new();
        public List<double> UserRadarValues { get; init; } = new();
    }
}
