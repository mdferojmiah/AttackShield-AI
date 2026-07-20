using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using MongoDB.Driver;

namespace AttackShield.Infrastructure.Persistence.Repositories;

public sealed class DetectionRepository : MongoRepository<Detection>, IDetectionRepository
{
    public DetectionRepository(MongoContext ctx) : base(ctx.Detections) { }

    public Task<Detection?> FindRecentAsync(
        string weaponType,
        string location,
        DateTime since,
        string? detectionType = null,
        CancellationToken ct = default)
    {
        var b = Builders<Detection>.Filter;
        var filter = b.Eq(d => d.WeaponType, weaponType)
                     & b.Eq(d => d.Location, location)
                     & b.Gte(d => d.CreatedAt, since);

        if (detectionType is not null)
            filter &= b.Eq(d => d.DetectionType, detectionType);

        return Collection.Find(filter).FirstOrDefaultAsync(ct)!;
    }

    public Task<long> CountByTypeAsync(string detectionType, CancellationToken ct = default)
        => Collection.CountDocumentsAsync(d => d.DetectionType == detectionType, cancellationToken: ct);

    public Task<long> CountByTypeSinceAsync(string detectionType, DateTime since, CancellationToken ct = default)
    {
        var b = Builders<Detection>.Filter;
        var filter = b.Eq(d => d.DetectionType, detectionType) & b.Gte(d => d.CreatedAt, since);
        return Collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<Detection>> GetRecentAsync(string? userId, int limit, CancellationToken ct = default)
    {
        var filter = userId is null
            ? Builders<Detection>.Filter.Empty
            : Builders<Detection>.Filter.Eq(d => d.UserId, userId);

        return await Collection.Find(filter)
            .SortByDescending(d => d.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);
    }
}
