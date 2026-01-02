using Microsoft.Extensions.FileProviders;
using SteamRec.ML;
using SteamRec.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddHttpClient<SteamProfileService>();
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<MongoDb>();
builder.Services.AddSingleton<GameRepository>();
builder.Services.AddSingleton<InteractionRepository>();
builder.Services.AddSingleton<IRecommenderProvider, RecommenderProvider>();

// Collaborative filtering
builder.Services.AddSingleton<CollaborativeFilteringRecommender>(sp =>
{
    var cf = new CollaborativeFilteringRecommender();

    try
    {
        // Train once at startup; the profile page can retrain after new interactions.
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

var webRootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(webRootPath),
    RequestPath = "/wwwroot"
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();
app.Run();
