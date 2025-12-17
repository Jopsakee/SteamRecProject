using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;

namespace SteamRec.Web.Services;

public class InteractionStore
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IWebHostEnvironment _env;

    // ✅ Tune this. Anything below this is treated as "not a real interaction".
    // 10–30 minutes is a good default.
    private const int MIN_TOTAL_MINUTES = 10;

    public InteractionStore(IWebHostEnvironment env)
    {
        _env = env;
    }

    public string GetInteractionsPath()
    {
        var path = Path.Combine(_env.ContentRootPath, "..", "..", "data", "processed", "interactions.csv");
        return Path.GetFullPath(path);
    }

    public async Task<bool> TryAddUserAsync(string steamId, IEnumerable<(int appid, int forever, int twoWeeks)> games)
    {
        steamId = (steamId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(steamId)) return false;

        var path = GetInteractionsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(path))
            {
                await File.WriteAllTextAsync(path, "steamid,appid,playtime_forever,playtime_2weeks\n");
            }

            // Don't add the same user repeatedly
            if (UserExists(path, steamId))
                return false;

            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(fs);

            int rows = 0;

            foreach (var (appid, forever, twoWeeks) in games)
            {
                if (appid <= 0) continue;

                int total = Math.Max(0, forever) + Math.Max(0, twoWeeks);

                // ✅ Skip 0-minutes and tiny/noise playtime
                if (total < MIN_TOTAL_MINUTES)
                    continue;

                writer.Write(steamId);
                writer.Write(',');
                writer.Write(appid.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(Math.Max(0, forever).ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(Math.Max(0, twoWeeks).ToString(CultureInfo.InvariantCulture));
                writer.Write('\n');

                rows++;
            }

            await writer.FlushAsync();
            return rows > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool UserExists(string path, string steamId)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);

        reader.ReadLine(); // header
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var idx = line.IndexOf(',');
            if (idx <= 0) continue;

            var first = line.Substring(0, idx);
            if (string.Equals(first, steamId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
