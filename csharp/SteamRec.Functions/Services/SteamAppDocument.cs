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

    // “Hydrated” means we already created/updated a row in games for this appid at least once
    public DateTime? HydratedUtc { get; set; }

    // Backoff
    public int FailureCount { get; set; }
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
}
