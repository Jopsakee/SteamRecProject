using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SteamRec.Functions.Services;

[BsonIgnoreExtraElements]
public class GameDocument
{
    // Works whether _id is an ObjectId or something else (we just ignore if not used)
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("appid")]
    public int AppId { get; set; }

    [BsonElement("name")]
    public string? Name { get; set; }

    // stored as semicolon-separated strings in your pipeline
    [BsonElement("genres")]
    public string? Genres { get; set; }

    [BsonElement("categories")]
    public string? Categories { get; set; }

    [BsonElement("tags")]
    public string? Tags { get; set; }

    [BsonElement("price_eur")]
    public double PriceEur { get; set; }

    [BsonElement("metacritic_score")]
    public double MetacriticScore { get; set; }

    [BsonElement("release_year")]
    public int ReleaseYear { get; set; }

    [BsonElement("required_age")]
    public int RequiredAge { get; set; }

    [BsonElement("is_free")]
    public bool IsFree { get; set; }

    [BsonElement("review_positive")]
    public int ReviewPositive { get; set; }

    [BsonElement("review_negative")]
    public int ReviewNegative { get; set; }

    [BsonElement("review_total")]
    public int ReviewTotal { get; set; }

    [BsonElement("review_ratio")]
    public double ReviewRatio { get; set; }

    [BsonElement("review_score_adj")]
    public double ReviewScoreAdj { get; set; }

    [BsonElement("updated_utc")]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
