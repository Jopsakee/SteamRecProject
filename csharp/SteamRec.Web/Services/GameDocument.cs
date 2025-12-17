using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SteamRec.Web.Services;

[BsonIgnoreExtraElements] // ✅ ignore fields we don't map (like ReviewPositive/ReviewNegative)
public class GameDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

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

    // these exist for sure
    public int ReviewTotal { get; set; }
    public double ReviewRatio { get; set; }
    public double ReviewScoreAdj { get; set; }
    public double ReviewVolumeLog { get; set; }
}
