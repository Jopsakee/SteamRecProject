using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;

namespace SteamRec.Core;

public static class GameDataLoader
{
    public static List<GameRecord> LoadGames(string csvPath)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException("games_clean.csv not found", csvPath);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null
        };

        using var reader = new StreamReader(csvPath);
        using var csv = new CsvReader(reader, config);

        // Ensure header is read so TryGetField works correctly
        csv.Read();
        csv.ReadHeader();

        var games = new List<GameRecord>();

        while (csv.Read())
        {
            try
            {
                var g = new GameRecord
                {
                    AppId = SafeGetInt(csv, "appid"),
                    Name = SafeGetString(csv, "name"),
                    GenresRaw = SafeGetString(csv, "genres"),
                    CategoriesRaw = SafeGetString(csv, "categories"),
                    TagsRaw = SafeGetString(csv, "tags"),
                    PriceEur = SafeGetDouble(csv, "price_eur"),
                    MetacriticScore = SafeGetDouble(csv, "metacritic_score"),
                    ReleaseYear = SafeGetInt(csv, "release_year"),
                    RequiredAge = SafeGetInt(csv, "required_age"),
                    IsFree = SafeGetInt(csv, "is_free") == 1,
                    ReviewTotal = SafeGetInt(csv, "review_total"),
                    ReviewRatio = SafeGetDouble(csv, "review_ratio"),
                    ReviewScoreAdj = SafeGetDouble(csv, "review_score_adj"),
                };

                g.Genres = SplitSemicolon(g.GenresRaw);
                g.Categories = SplitSemicolon(g.CategoriesRaw);
                g.Tags = SplitSemicolon(g.TagsRaw);

                // Basic sanity: require at least appid and name
                if (g.AppId > 0 && !string.IsNullOrWhiteSpace(g.Name))
                {
                    games.Add(g);
                }
            }
            catch
            {
                // Skip this row if something unexpected happens
            }
        }

        return games;
    }

    private static string SafeGetString(CsvReader csv, string name)
    {
        return csv.TryGetField<string>(name, out var value)
            ? (value ?? "")
            : "";
    }

    private static int SafeGetInt(CsvReader csv, string name)
    {
        if (!csv.TryGetField<string>(name, out var s) || string.IsNullOrWhiteSpace(s))
            return 0;

        return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;
    }

    private static double SafeGetDouble(CsvReader csv, string name)
    {
        if (!csv.TryGetField<string>(name, out var s) || string.IsNullOrWhiteSpace(s))
            return 0.0;

        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0.0;
    }

    private static HashSet<string> SplitSemicolon(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return s.Split(';')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
