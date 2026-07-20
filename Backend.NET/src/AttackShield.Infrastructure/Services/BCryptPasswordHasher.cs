using AttackShield.Core.Interfaces;

namespace AttackShield.Infrastructure.Services;

/// <summary>
/// BCrypt hashing at cost factor 12 — identical work factor to the original
/// bcryptjs `genSalt(12)`, so hashes are cross-compatible in either direction.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(hash))
            return false;
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (Exception ex) when (ex is BCrypt.Net.SaltParseException or ArgumentException or FormatException)
        {
            // Malformed/legacy/truncated hash — treat as a failed verification rather
            // than throwing. BCrypt.Net signals bad input via several exception types
            // (SaltParseException, ArgumentOutOfRangeException, FormatException).
            return false;
        }
    }
}
