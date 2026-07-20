using AttackShield.Core.Entities;

namespace AttackShield.Core.Interfaces;

public interface IAuthorityRepository : IRepository<Authority>
{
    Task<Authority?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<Authority?> GetByEmailOrOfficerIdAsync(string email, string officerId, CancellationToken ct = default);
}
