namespace AttackShield.Infrastructure.Persistence;

/// <summary>Bound from the "Mongo" configuration section.</summary>
public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string Database { get; set; } = "weapon-detection";
}
