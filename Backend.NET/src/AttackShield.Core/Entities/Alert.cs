using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AttackShield.Core.Entities;

/// <summary>
/// Actionable alert raised from a weapon detection and worked by an authority.
/// Maps to "alerts". Mongoose { timestamps: true } => createdAt / updatedAt.
/// </summary>
public class Alert
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    // high | medium | low
    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    [BsonElement("title")]
    [BsonIgnoreIfNull]
    public string? Title { get; set; }

    [BsonElement("location")]
    [BsonIgnoreIfNull]
    public string? Location { get; set; }

    [BsonElement("imageUrl")]
    [BsonIgnoreIfNull]
    public string? ImageUrl { get; set; }

    [BsonElement("cameraName")]
    [BsonIgnoreIfNull]
    public string? CameraName { get; set; }

    [BsonElement("detectionId")]
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? DetectionId { get; set; }

    [BsonElement("userId")]
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? UserId { get; set; }

    // new | accepted | dismissed | resolved
    [BsonElement("status")]
    public string Status { get; set; } = "new";

    [BsonElement("assignedTo")]
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? AssignedTo { get; set; }

    [BsonElement("acceptedAt")]
    [BsonIgnoreIfNull]
    public DateTime? AcceptedAt { get; set; }

    [BsonElement("resolvedAt")]
    [BsonIgnoreIfNull]
    public DateTime? ResolvedAt { get; set; }

    [BsonElement("isRead")]
    public bool IsRead { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
