using AttackShield.Core.Entities;

namespace AttackShield.Core.Interfaces;

public interface IStatsRepository : IRepository<Stats>
{
    /// <summary>Returns the single stats document, creating a default one if absent.</summary>
    Task<Stats> GetOrCreateAsync(CancellationToken ct = default);
}
