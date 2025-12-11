using System;
using System.Collections.Generic;

namespace SteamRec.Core;

public class GameRecord
{
    public int AppId { get; set; }
    public string Name { get; set; } = "";

    public string GenresRaw { get; set; } = "";
    public string CategoriesRaw { get; set; } = "";
    public string TagsRaw { get; set; } = "";

    public double PriceEur { get; set; }
    public double MetacriticScore { get; set; }
    public int ReleaseYear { get; set; }
    public int RequiredAge { get; set; }
    public bool IsFree { get; set; }

    public int ReviewTotal { get; set; }
    public double ReviewRatio { get; set; }
    public double ReviewScoreAdj { get; set; }

    public HashSet<string> Genres { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Categories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public double ReviewVolumeLog => Math.Log10(ReviewTotal + 1);

    public double[] Features { get; set; } = Array.Empty<double>();
}
