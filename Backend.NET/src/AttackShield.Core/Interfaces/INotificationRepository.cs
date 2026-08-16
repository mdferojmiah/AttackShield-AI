using AttackShield.Core.Entities;

namespace AttackShield.Core.Interfaces;

public interface INotificationRepository : IRepository<Notification>
{
    /// <summary>Notifications for a user, or all notifications when userId is null.</summary>
    Task<IReadOnlyList<Notification>> GetAllNewestAsync(string? userId = null, CancellationToken ct = default);

    Task<bool> MarkReadAsync(string id, CancellationToken ct = default);

    Task<long> CountByTypesAsync(IEnumerable<string> types, string? userId = null, CancellationToken ct = default);
}
