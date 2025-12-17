using SteamRec.Import;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var csvPath = Path.Combine(repoRoot, "data", "processed", "games_clean.csv");

await ImportGamesToMongo.RunAsync(csvPath);
