using AttackShield.Core.Entities;

namespace AttackShield.Core.Interfaces;

public interface INotificationRepository : IRepository<Notification>
{
    /// <summary>All notifications, newest first (original controller returns them un-scoped).</summary>
    Task<IReadOnlyList<Notification>> GetAllNewestAsync(CancellationToken ct = default);

    Task<bool> MarkReadAsync(string id, CancellationToken ct = default);

    Task<long> CountByTypesAsync(IEnumerable<string> types, CancellationToken ct = default);
}
