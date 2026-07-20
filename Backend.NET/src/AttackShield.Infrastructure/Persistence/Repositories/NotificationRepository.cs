using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using MongoDB.Driver;

namespace AttackShield.Infrastructure.Persistence.Repositories;

public sealed class NotificationRepository : MongoRepository<Notification>, INotificationRepository
{
    public NotificationRepository(MongoContext ctx) : base(ctx.Notifications) { }

    public async Task<IReadOnlyList<Notification>> GetAllNewestAsync(CancellationToken ct = default)
        => await Collection.Find(Builders<Notification>.Filter.Empty)
            .SortByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

    public async Task<bool> MarkReadAsync(string id, CancellationToken ct = default)
    {
        var update = Builders<Notification>.Update.Set(n => n.IsRead, true);
        var result = await Collection.UpdateOneAsync(n => n.Id == id, update, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public Task<long> CountByTypesAsync(IEnumerable<string> types, CancellationToken ct = default)
    {
        var filter = Builders<Notification>.Filter.In(n => n.Type, types);
        return Collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }
}
