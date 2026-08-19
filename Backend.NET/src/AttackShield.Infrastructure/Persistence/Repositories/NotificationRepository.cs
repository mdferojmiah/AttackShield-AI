using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using MongoDB.Driver;

namespace AttackShield.Infrastructure.Persistence.Repositories;

public sealed class NotificationRepository : MongoRepository<Notification>, INotificationRepository
{
    public NotificationRepository(MongoContext ctx) : base(ctx.Notifications) { }

    public async Task<IReadOnlyList<Notification>> GetAllNewestAsync(string? userId = null, CancellationToken ct = default)
        => await Collection.Find(userId is null
                ? Builders<Notification>.Filter.Empty
                : Builders<Notification>.Filter.Eq(n => n.UserId, userId))
            .SortByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

    public async Task<NotificationPage> GetPageAsync(
        string? userId,
        DateTime? before,
        string? beforeId,
        int limit,
        CancellationToken ct = default)
    {
        var b = Builders<Notification>.Filter;
        var filter = userId is null ? b.Empty : b.Eq(n => n.UserId, userId);

        if (before is not null)
        {
            // Detections fire in bursts, so timestamps collide; _id breaks the tie and
            // stops a same-millisecond sibling from being skipped between pages.
            var older = b.Lt(n => n.CreatedAt, before.Value);
            if (beforeId is not null)
                older |= b.Eq(n => n.CreatedAt, before.Value) & b.Lt(n => n.Id, beforeId);
            filter &= older;
        }

        var sort = Builders<Notification>.Sort.Descending(n => n.CreatedAt).Descending(n => n.Id);

        // One extra row tells us whether another page exists without a second count query.
        var rows = await Collection.Find(filter).Sort(sort).Limit(limit + 1).ToListAsync(ct);

        return rows.Count > limit
            ? new NotificationPage(rows.GetRange(0, limit), true)
            : new NotificationPage(rows, false);
    }

    public async Task<bool> MarkReadAsync(string id, CancellationToken ct = default)
    {
        var update = Builders<Notification>.Update.Set(n => n.IsRead, true);
        var result = await Collection.UpdateOneAsync(n => n.Id == id, update, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public Task<long> CountByTypesAsync(IEnumerable<string> types, string? userId = null, CancellationToken ct = default)
    {
        var filter = Builders<Notification>.Filter.In(n => n.Type, types);
        if (userId is not null)
            filter &= Builders<Notification>.Filter.Eq(n => n.UserId, userId);
        return Collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }
}
