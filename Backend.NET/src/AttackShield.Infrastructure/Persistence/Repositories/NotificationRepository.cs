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
