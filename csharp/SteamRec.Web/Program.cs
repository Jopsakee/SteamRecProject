using SteamRec.Core;
using SteamRec.ML;
using SteamRec.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddHttpClient<SteamProfileService>();

// interaction store (writes interactions.csv)
builder.Services.AddSingleton<InteractionStore>();

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
// NOTE: trained at app startup only
builder.Services.AddSingleton<CollaborativeFilteringRecommender>(sp =>
{
    var store = sp.GetRequiredService<InteractionStore>();
    var interactionsPath = store.GetInteractionsPath();

    var cf = new CollaborativeFilteringRecommender();

    try
    {
        Console.WriteLine($"[SteamRec] Loading interactions from: {interactionsPath}");
        cf.TrainFromCsv(interactionsPath);
        Console.WriteLine("[SteamRec] Collaborative model trained OK.");
    }
    catch (Exception ex)
    {
        Console.WriteLine("[SteamRec] Collaborative model unavailable: " + ex.Message);
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
