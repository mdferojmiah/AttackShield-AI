using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AttackShield.Core.Entities;

/// <summary>
/// Application user (CCTV operator). Maps to the "users" collection.
/// Document shape is preserved from the original Mongoose model, including the
/// EMBEDDED cameras array and nested settings sub-document — no separate collections.
/// </summary>
public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    // sparse unique in Mongo — omitted from the document when null so the sparse
    // index does not treat multiple nulls as duplicates.
    [BsonElement("googleId")]
    [BsonIgnoreIfNull]
    public string? GoogleId { get; set; }

    [BsonElement("avatar")]
    [BsonIgnoreIfNull]
    public string? Avatar { get; set; }

    [BsonElement("phone")]
    [BsonIgnoreIfNull]
    public string? Phone { get; set; }

    // Hashed with BCrypt (cost 12). Never returned to clients — map through a DTO.
    [BsonElement("password")]
    [BsonIgnoreIfNull]
    public string? Password { get; set; }

    [BsonElement("cctvName")]
    [BsonIgnoreIfNull]
    public string? CctvName { get; set; }

    [BsonElement("rtspUrl")]
    [BsonIgnoreIfNull]
    public string? RtspUrl { get; set; }

    [BsonElement("location")]
    [BsonIgnoreIfNull]
    public string? Location { get; set; }

    // Extra cameras beyond the primary one. Embedded documents, not a reference.
    [BsonElement("cameras")]
    public List<EmbeddedCamera> Cameras { get; set; } = new();

    [BsonElement("role")]
    public string Role { get; set; } = "user";

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("lastLogin")]
    [BsonIgnoreIfNull]
    public DateTime? LastLogin { get; set; }

    [BsonElement("passwordResetToken")]
    [BsonIgnoreIfNull]
    public string? PasswordResetToken { get; set; }

    [BsonElement("passwordResetExpires")]
    [BsonIgnoreIfNull]
    public DateTime? PasswordResetExpires { get; set; }

    [BsonElement("settings")]
    public UserSettings Settings { get; set; } = new();

    // Mongoose { timestamps: true } => createdAt / updatedAt.
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Embedded camera document inside <see cref="User.Cameras"/>.</summary>
public class EmbeddedCamera
{
    // Mongo assigns an _id to array sub-documents by default; keep it so the
    // frontend can address extra cameras by id (see camerasController).
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("rtspUrl")]
    public string RtspUrl { get; set; } = string.Empty;

    [BsonElement("location")]
    public string Location { get; set; } = string.Empty;

    [BsonElement("brand")]
    [BsonIgnoreIfNull]
    public string? Brand { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UserSettings
{
    [BsonElement("notifications")]
    public NotificationSettings Notifications { get; set; } = new();

    [BsonElement("detection")]
    public DetectionSettings Detection { get; set; } = new();

    [BsonElement("app")]
    public AppSettings App { get; set; } = new();
}

public class NotificationSettings
{
    [BsonElement("push")]
    public bool Push { get; set; } = true;

    [BsonElement("sound")]
    public bool Sound { get; set; } = true;

    [BsonElement("vibration")]
    public bool Vibration { get; set; } = true;
}

public class DetectionSettings
{
    // low | medium | high | max
    [BsonElement("sensitivity")]
    public string Sensitivity { get; set; } = "high";

    [BsonElement("alertThreshold")]
    public int AlertThreshold { get; set; } = 5;

    [BsonElement("autoStartMonitoring")]
    public bool AutoStartMonitoring { get; set; } = false;
}

public class AppSettings
{
    // dark | light | system
    [BsonElement("theme")]
    public string Theme { get; set; } = "dark";
}
