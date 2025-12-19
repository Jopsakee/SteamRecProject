using SteamRec.Core;
using SteamRec.ML;
using SteamRec.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient<SteamProfileService>();

builder.Services.AddSingleton<MongoDb>();
builder.Services.AddSingleton<GameRepository>();
builder.Services.AddSingleton<InteractionRepository>();

// Content-based recommender
builder.Services.AddSingleton<ContentBasedRecommender>(sp =>
{
    var repo = sp.GetRequiredService<GameRepository>();
    var docs = repo.GetAllAsync().GetAwaiter().GetResult();

    if (docs.Count == 0)
        throw new InvalidOperationException("[SteamRec] MongoDB returned 0 games. Did you import games into the 'games' collection?");

    Console.WriteLine($"[SteamRec] Loaded {docs.Count} games from MongoDB.");

    static HashSet<string> SplitSemicolon(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return s.Split(';')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    var games = docs.Select(d =>
    {
        var g = new GameRecord
        {
            AppId = d.AppId,
            Name = d.Name ?? "",
            GenresRaw = d.Genres ?? "",
            CategoriesRaw = d.Categories ?? "",
            TagsRaw = d.Tags ?? "",
            PriceEur = d.PriceEur,
            MetacriticScore = d.MetacriticScore,
            ReleaseYear = d.ReleaseYear,
            RequiredAge = d.RequiredAge,
            IsFree = d.IsFree,
            ReviewTotal = d.ReviewTotal,
            ReviewRatio = d.ReviewRatio,
            ReviewScoreAdj = d.ReviewScoreAdj,
        };

        g.Genres = SplitSemicolon(g.GenresRaw);
        g.Categories = SplitSemicolon(g.CategoriesRaw);
        g.Tags = SplitSemicolon(g.TagsRaw);

        return g;
    }).ToList();

    return new ContentBasedRecommender(games);
});

// Collaborative filtering
builder.Services.AddSingleton<CollaborativeFilteringRecommender>(sp =>
{
    var cf = new CollaborativeFilteringRecommender();

    try
    {
        var repo = sp.GetRequiredService<InteractionRepository>();
        var interactions = repo.GetAllAsync().GetAwaiter().GetResult();

        Console.WriteLine($"[SteamRec] Loaded {interactions.Count} interactions from MongoDB.");

        var rows = interactions.Select(i => (
            steamId: i.SteamId,
            appId: (uint)i.AppId,
            playtimeForever: i.PlaytimeForever,
            playtime2Weeks: i.Playtime2Weeks
        ));

        cf.TrainFromRows(rows);
        Console.WriteLine("[SteamRec] Collaborative model trained from MongoDB.");
    }
    catch (Exception ex)
    {
        Console.WriteLine("[SteamRec] Collaborative model not ready: " + ex.Message);
    }

    return cf;
});

var app = builder.Build();

// Force singletons to initialize at startup
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ContentBasedRecommender>();
    scope.ServiceProvider.GetRequiredService<CollaborativeFilteringRecommender>();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();
app.Run();
