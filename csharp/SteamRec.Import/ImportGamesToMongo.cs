using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace SteamRec.Import;

public static class ImportGamesToMongo
{
    private class GameDocument
    {
        public int AppId { get; set; }
        public string Name { get; set; } = "";

        public string Genres { get; set; } = "";
        public string Categories { get; set; } = "";
        public string Tags { get; set; } = "";

        public double PriceEur { get; set; }
        public double MetacriticScore { get; set; }
        public int ReleaseYear { get; set; }
        public int RequiredAge { get; set; }
        public bool IsFree { get; set; }

        public int ReviewPositive { get; set; }
        public int ReviewNegative { get; set; }
        public int ReviewTotal { get; set; }
        public double ReviewRatio { get; set; }
        public double ReviewScoreAdj { get; set; }

        public double ReviewVolumeLog { get; set; }
    }

    // Read "problem columns" as strings so we can parse 2000.0 safely.
    private class Row
    {
        public string appid { get; set; } = "";
        public string? name { get; set; }
        public string? release_date { get; set; }

        public string? release_year { get; set; }
        public string? price_eur { get; set; }
        public string? metacritic_score { get; set; }
        public string? required_age { get; set; }
        public string? is_free { get; set; }

        public string? genres { get; set; }
        public string? categories { get; set; }
        public string? tags { get; set; }

        public string? review_positive { get; set; }
        public string? review_negative { get; set; }
        public string? review_total { get; set; }
        public string? review_ratio { get; set; }
        public string? review_score_adj { get; set; }
    }

    public static async Task RunAsync(string csvPath)
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var cs = config["Mongo:ConnectionString"] ?? Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
        var dbName = config["Mongo:Database"] ?? Environment.GetEnvironmentVariable("MONGODB_DATABASE") ?? "steamrec";

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Mongo connection string not configured. Set Mongo:ConnectionString (user-secrets) or MONGODB_CONNECTION_STRING.");

        var client = new MongoClient(cs);
        var db = client.GetDatabase(dbName);

        var games = db.GetCollection<GameDocument>("games");

        if (!File.Exists(csvPath))
            throw new FileNotFoundException("games_clean.csv not found", csvPath);

        Console.WriteLine($"[Import] CSV: {csvPath}");
        Console.WriteLine($"[Import] DB: {dbName}, collection: games");

        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null
        };

        using var reader = new StreamReader(csvPath);
        using var csv = new CsvReader(reader, csvConfig);

        var batch = new List<GameDocument>(5000);
        int total = 0;
        int skipped = 0;

        await foreach (var r in csv.GetRecordsAsync<Row>())
        {
            int appId = ParseInt(r.appid);
            if (appId <= 0) { skipped++; continue; }

            int releaseYear = ParseInt(r.release_year);
            int requiredAge = ParseInt(r.required_age);
            bool isFree = ParseInt(r.is_free) == 1;

            double priceEur = ParseDouble(r.price_eur);
            double meta = ParseDouble(r.metacritic_score);

            int revPos = ParseInt(r.review_positive);
            int revNeg = ParseInt(r.review_negative);
            int revTotal = ParseInt(r.review_total);
            double revRatio = ParseDouble(r.review_ratio);
            double revScoreAdj = ParseDouble(r.review_score_adj);

            batch.Add(new GameDocument
            {
                AppId = appId,
                Name = r.name ?? "",

                Genres = r.genres ?? "",
                Categories = r.categories ?? "",
                Tags = r.tags ?? "",

                PriceEur = priceEur,
                MetacriticScore = meta,
                ReleaseYear = releaseYear,
                RequiredAge = requiredAge,
                IsFree = isFree,

                ReviewPositive = Math.Max(0, revPos),
                ReviewNegative = Math.Max(0, revNeg),
                ReviewTotal = Math.Max(0, revTotal),
                ReviewRatio = revRatio,
                ReviewScoreAdj = revScoreAdj,

                ReviewVolumeLog = Math.Log10(1.0 + Math.Max(0, revTotal))
            });

            if (batch.Count >= 5000)
            {
                await BulkUpsertAsync(games, batch);
                total += batch.Count;
                Console.WriteLine($"[Import] Upserted {total} games...");
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await BulkUpsertAsync(games, batch);
            total += batch.Count;
        }

        Console.WriteLine($"[Import] Done. Upserted {total} games. Skipped {skipped} rows.");
    }

    private static Task BulkUpsertAsync(IMongoCollection<GameDocument> col, List<GameDocument> docs)
    {
        var models = docs.Select(d =>
            new ReplaceOneModel<GameDocument>(
                Builders<GameDocument>.Filter.Eq(x => x.AppId, d.AppId),
                d)
            { IsUpsert = true }
        ).ToList();

        return col.BulkWriteAsync(models);
    }

    private static int ParseInt(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;

        // Handles "2000", "2000.0", "255406", "248818.0"
        if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
            return i;

        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return (int)Math.Round(d);

        return 0;
    }

    private static double ParseDouble(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0.0;

        if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return d;

        return 0.0;
    }
}
