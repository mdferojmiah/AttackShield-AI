using AttackShield.Core.Entities;

namespace AttackShield.Core.Interfaces;

/// <summary>One newest-first page of notifications. <paramref name="HasMore"/> is true when older rows remain.</summary>
public sealed record NotificationPage(IReadOnlyList<Notification> Items, bool HasMore);

public interface INotificationRepository : IRepository<Notification>
{
    /// <summary>Notifications for a user, or all notifications when userId is null.</summary>
    Task<IReadOnlyList<Notification>> GetAllNewestAsync(string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Newest-first page of at most <paramref name="limit"/> notifications. When
    /// <paramref name="before"/>/<paramref name="beforeId"/> are supplied, only rows ordering
    /// strictly after that cursor are returned, so concurrent inserts at the head of the feed
    /// cannot shift or duplicate results across pages.
    /// </summary>
    Task<NotificationPage> GetPageAsync(
        string? userId,
        DateTime? before,
        string? beforeId,
        int limit,
        CancellationToken ct = default);

    Task<bool> MarkReadAsync(string id, CancellationToken ct = default);

    Task<long> CountByTypesAsync(IEnumerable<string> types, string? userId = null, CancellationToken ct = default);
}
