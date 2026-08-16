using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using MongoDB.Driver;

namespace AttackShield.Infrastructure.Persistence.Repositories;

public sealed class AlertRepository : MongoRepository<Alert>, IAlertRepository
{
    public AlertRepository(MongoContext ctx) : base(ctx.Alerts) { }

    public async Task<IReadOnlyList<Alert>> GetNewAsync(CancellationToken ct = default)
        => await Collection.Find(a => a.Status == "new")
            .SortByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Alert>> GetActiveForAuthorityAsync(string authorityId, CancellationToken ct = default)
    {
        var filter = Builders<Alert>.Filter.Eq(a => a.AssignedTo, authorityId)
                     & Builders<Alert>.Filter.Eq(a => a.Status, "accepted");
        return await Collection.Find(filter)
            .SortByDescending(a => a.AcceptedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Alert>> GetHistoryForAuthorityAsync(string authorityId, CancellationToken ct = default)
    {
        var filter = Builders<Alert>.Filter.Eq(a => a.AssignedTo, authorityId)
                     & Builders<Alert>.Filter.In(a => a.Status, new[] { "accepted", "dismissed", "resolved" });
        return await Collection.Find(filter)
            .SortByDescending(a => a.CreatedAt)
            .ToListAsync(ct);
    }
}
