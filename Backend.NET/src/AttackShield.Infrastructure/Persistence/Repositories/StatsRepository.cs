using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using MongoDB.Driver;

namespace AttackShield.Infrastructure.Persistence.Repositories;

public sealed class StatsRepository : MongoRepository<Stats>, IStatsRepository
{
    public StatsRepository(MongoContext ctx) : base(ctx.Stats) { }

    public async Task<Stats> GetOrCreateAsync(CancellationToken ct = default)
    {
        var existing = await Collection.Find(Builders<Stats>.Filter.Empty).FirstOrDefaultAsync(ct);
        if (existing is not null)
            return existing;

        var stats = new Stats();
        await Collection.InsertOneAsync(stats, cancellationToken: ct);
        return stats;
    }
}
