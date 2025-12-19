using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SteamRec.Web.Services;

[BsonIgnoreExtraElements]
public class InteractionDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    public string SteamId { get; set; } = "";
    public int AppId { get; set; }

    // minutes
    public float PlaytimeForever { get; set; }
    public float Playtime2Weeks { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
