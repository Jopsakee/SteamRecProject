using SteamRec.Core;
using SteamRec.ML;
using SteamRec.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// HttpClient for Steam profile service
builder.Services.AddHttpClient<SteamProfileService>();

// Content-based recommender singleton (games_clean.csv)
builder.Services.AddSingleton<ContentBasedRecommender>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();

    var csvPath = Path.Combine(env.ContentRootPath, "..", "..", "data", "processed", "games_clean.csv");
    csvPath = Path.GetFullPath(csvPath);

    Console.WriteLine($"[SteamRec] Loading games from: {csvPath}");

    var games = GameDataLoader.LoadGames(csvPath);
    Console.WriteLine($"[SteamRec] Loaded {games.Count} games from CSV.");

    return new ContentBasedRecommender(games);
});

// Collaborative filtering model singleton (interactions.csv)
builder.Services.AddSingleton<CollaborativeFilteringRecommender>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();

    var interactionsPath = Path.Combine(env.ContentRootPath, "..", "..", "data", "processed", "interactions.csv");
    interactionsPath = Path.GetFullPath(interactionsPath);

    var cf = new CollaborativeFilteringRecommender();

    try
    {
        Console.WriteLine($"[SteamRec] Loading interactions from: {interactionsPath}");
        cf.TrainFromCsv(interactionsPath);
        Console.WriteLine("[SteamRec] Collaborative model trained OK.");

        var eval = cf.EvaluateHitRateAtK(interactionsPath, k: 20);
        Console.WriteLine($"[SteamRec] CF HitRate@20={eval.hitRateAtK:0.000} Recall@20={eval.recallAtK:0.000} Users={eval.usersEvaluated}");
    }
    catch (Exception ex)
    {
        Console.WriteLine("[SteamRec] Collaborative model unavailable: " + ex.Message);
        // Keep instance; IsReady will be false if training failed
    }

    return cf;
});

var app = builder.Build();

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
