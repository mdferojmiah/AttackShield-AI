using AttackShield.Core.Entities;

namespace AttackShield.Core.Interfaces;

public interface IAlertRepository : IRepository<Alert>
{
    /// <summary>Unassigned alerts (status = new), newest first.</summary>
    Task<IReadOnlyList<Alert>> GetNewAsync(CancellationToken ct = default);

    /// <summary>Alerts accepted by a given authority, most recently accepted first.</summary>
    Task<IReadOnlyList<Alert>> GetActiveForAuthorityAsync(string authorityId, CancellationToken ct = default);

    /// <summary>Dismissed/resolved history for a given authority.</summary>
    Task<IReadOnlyList<Alert>> GetHistoryForAuthorityAsync(string authorityId, CancellationToken ct = default);
}
