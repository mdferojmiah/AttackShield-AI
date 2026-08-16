using AttackShield.Core.Entities;

namespace AttackShield.Core.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken ct = default);

    /// <summary>Sets lastLogin without triggering a full validation cycle.</summary>
    Task TouchLastLoginAsync(string id, DateTime when, CancellationToken ct = default);
}
