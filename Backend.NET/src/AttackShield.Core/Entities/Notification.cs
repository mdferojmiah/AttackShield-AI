using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AttackShield.Core.Entities;

/// <summary>
/// User-facing notification. Maps to "notifications".
/// type: suspicious | vehicle | loitering | package | camera | weapon | system | face | activity
/// </summary>
public class Notification
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("icon")]
    public string Icon { get; set; } = "alert-circle";

    [BsonElement("location")]
    [BsonIgnoreIfNull]
    public string? Location { get; set; }

    [BsonElement("userId")]
    [BsonIgnoreIfNull]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? UserId { get; set; }

    [BsonElement("isRead")]
    public bool IsRead { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
