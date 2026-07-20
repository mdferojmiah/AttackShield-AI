namespace AttackShield.Infrastructure.Services;

/// <summary>Bound from the "Ai" configuration section.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string BaseUrl { get; set; } = "http://localhost:8000";
}
