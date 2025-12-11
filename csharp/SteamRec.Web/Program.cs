using SteamRec.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// register recommender singleton
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
