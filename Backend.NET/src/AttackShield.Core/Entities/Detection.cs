using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AttackShield.Core.Entities;

/// <summary>
/// A single detection event pushed by the AI service. Maps to "detections".
/// High-volume collection (per-frame inserts) — kept intentionally lean.
/// </summary>
public class Detection
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("weaponType")]
    public string WeaponType { get; set; } = string.Empty;

    [BsonElement("location")]
    public string Location { get; set; } = string.Empty;

    // 0..1
    [BsonElement("confidence")]
    public double Confidence { get; set; }

    [BsonElement("imageUrl")]
    [BsonIgnoreIfNull]
    public string? ImageUrl { get; set; }

    [BsonElement("cameraName")]
    [BsonIgnoreIfNull]
    public string? CameraName { get; set; }

    // weapon | suspicious_activity | face
    [BsonElement("detectionType")]
    public string DetectionType { get; set; } = "weapon";

    // Optional ref to User; only set when a valid ObjectId was supplied.
    [BsonElement("userId")]
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? UserId { get; set; }

    // Detection model uses a bare createdAt (no Mongoose timestamps pair).
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
