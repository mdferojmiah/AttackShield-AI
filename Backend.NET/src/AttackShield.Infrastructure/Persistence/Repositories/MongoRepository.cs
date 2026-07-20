using System.Linq.Expressions;
using AttackShield.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AttackShield.Infrastructure.Persistence.Repositories;

/// <summary>
/// Generic MongoDB-backed repository. Assumes the entity has a string Id mapped
/// to _id (ObjectId). Update replaces the whole document by _id.
/// </summary>
public class MongoRepository<T> : IRepository<T> where T : class
{
    protected readonly IMongoCollection<T> Collection;

    public MongoRepository(IMongoCollection<T> collection)
    {
        Collection = collection;
    }

    public async Task<T?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var filter = Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
        return await Collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
        => await Collection.Find(filter).ToListAsync(ct);

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        => await Collection.Find(Builders<T>.Filter.Empty).ToListAsync(ct);

    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
        => await Collection.Find(filter).FirstOrDefaultAsync(ct);

    public async Task InsertAsync(T entity, CancellationToken ct = default)
        => await Collection.InsertOneAsync(entity, cancellationToken: ct);

    public async Task<bool> UpdateAsync(string id, T entity, CancellationToken ct = default)
    {
        if (!ObjectId.TryParse(id, out _))
            return false;

        var filter = Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
        var result = await Collection.ReplaceOneAsync(filter, entity, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (!ObjectId.TryParse(id, out _))
            return false;

        var filter = Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
        var result = await Collection.DeleteOneAsync(filter, ct);
        return result.DeletedCount > 0;
    }

    public async Task<long> CountAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default)
        => await Collection.CountDocumentsAsync(filter, cancellationToken: ct);
}
