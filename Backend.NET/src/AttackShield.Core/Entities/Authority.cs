using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace AttackShield.Core.Entities;

/// <summary>
/// Law-enforcement account mapped to a station. Maps to the "authorities" collection.
/// Logs in through the same /api/auth/login endpoint as a <see cref="User"/>.
/// </summary>
public class Authority
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    // Stored upper-cased, unique.
    [BsonElement("officerId")]
    public string OfficerId { get; set; } = string.Empty;

    [BsonElement("stationName")]
    public string StationName { get; set; } = string.Empty;

    // BCrypt hash (cost 12). Never serialized to clients.
    [BsonElement("password")]
    [BsonIgnoreIfNull]
    public string? Password { get; set; }

    // authority | senior_authority | admin
    [BsonElement("role")]
    public string Role { get; set; } = "authority";

    [BsonElement("department")]
    public string Department { get; set; } = "Police";

    [BsonElement("isVerified")]
    public bool IsVerified { get; set; } = false;

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("assignedUsers")]
    [BsonRepresentation(BsonType.ObjectId)]
    public List<string> AssignedUsers { get; set; } = new();

    [BsonElement("lastLogin")]
    [BsonIgnoreIfNull]
    public DateTime? LastLogin { get; set; }

    [BsonElement("passwordResetToken")]
    [BsonIgnoreIfNull]
    public string? PasswordResetToken { get; set; }

    [BsonElement("passwordResetExpires")]
    [BsonIgnoreIfNull]
    public DateTime? PasswordResetExpires { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
