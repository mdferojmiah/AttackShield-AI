using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AttackShield.Core.Entities;

/// <summary>
/// Persisted aggregate counters. Maps to "stats".
/// Live dashboard numbers are computed from detections/notifications; this
/// collection is kept for parity with the original model.
/// </summary>
public class Stats
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("totalWeapons")]
    public int TotalWeapons { get; set; } = 0;

    [BsonElement("alertsSent")]
    public int AlertsSent { get; set; } = 0;

    // 0..1
    [BsonElement("accuracy")]
    public double Accuracy { get; set; } = 0.98;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
