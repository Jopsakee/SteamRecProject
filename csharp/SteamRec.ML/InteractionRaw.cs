using Microsoft.ML.Data;

namespace SteamRec.ML;

// Matches your interactions.csv exactly:
// steamid,appid,playtime_forever,playtime_2weeks
public class InteractionRaw
{
    [LoadColumn(0)]
    public string SteamId { get; set; } = "";

    [LoadColumn(1)]
    public uint AppId { get; set; }

    [LoadColumn(2)]
    public float PlaytimeForever { get; set; }

    [LoadColumn(3)]
    public float Playtime2Weeks { get; set; }
}
