namespace AttackShield.Infrastructure.Services;

/// <summary>Bound from the "Jwt" configuration section.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "AttackShield";
    public string Audience { get; set; } = "AttackShield";
    public int ExpireDays { get; set; } = 7;
}
