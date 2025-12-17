using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SteamRec.Functions.Services;

[BsonIgnoreExtraElements]
public class GameDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    public int AppId { get; set; }
    public string Name { get; set; } = "";

    public string? Genres { get; set; }
    public string? Categories { get; set; }
    public string? Tags { get; set; }

    public float PriceEur { get; set; }
    public float MetacriticScore { get; set; }
    public int ReleaseYear { get; set; }
    public int RequiredAge { get; set; }
    public bool IsFree { get; set; }

    public float ReviewPositive { get; set; }
    public float ReviewNegative { get; set; }
    public int ReviewTotal { get; set; }
    public double ReviewRatio { get; set; }
    public double ReviewScoreAdj { get; set; }

    public double ReviewVolumeLog { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
