namespace AttackShield.Core.Interfaces;

/// <summary>BCrypt password hashing (cost 12, matching the original bcryptjs setup).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
