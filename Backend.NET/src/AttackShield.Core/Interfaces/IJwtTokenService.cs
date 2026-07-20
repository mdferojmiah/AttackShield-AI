namespace AttackShield.Core.Interfaces;

/// <summary>
/// Issues the app's JWT. Payload mirrors the original Node service: { id, role }
/// with the id carried as the subject claim.
/// </summary>
public interface IJwtTokenService
{
    string GenerateToken(string userId, string role);
}
