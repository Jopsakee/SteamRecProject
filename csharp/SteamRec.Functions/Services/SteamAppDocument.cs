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

    // discovery bookkeeping
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    // hydration bookkeeping (games collection)
    public DateTime? HydratedUtc { get; set; }

    // retry/backoff bookkeeping
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public int FailureCount { get; set; }
}
