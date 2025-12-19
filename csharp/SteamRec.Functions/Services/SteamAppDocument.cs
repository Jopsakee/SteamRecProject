using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SteamRec.Functions.Services;

[BsonIgnoreExtraElements]
public class SteamAppDocument
{
    [BsonId]
    public ObjectId Id { get; set; }

    public int AppId { get; set; }
    public string Name { get; set; } = "";

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    public DateTime? HydratedUtc { get; set; }

    public int FailureCount { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
}
