using System.Linq.Expressions;

namespace AttackShield.Core.Interfaces;

/// <summary>
/// Generic MongoDB repository. Each entity type maps to one collection.
/// Domain-specific queries live on the derived interfaces.
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<T>> FindAsync(
        Expression<Func<T, bool>> filter,
        CancellationToken ct = default);

    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);

    Task<T?> FindOneAsync(
        Expression<Func<T, bool>> filter,
        CancellationToken ct = default);

    Task InsertAsync(T entity, CancellationToken ct = default);

    /// <summary>Replaces the whole document by its Id. Returns true if a document matched.</summary>
    Task<bool> UpdateAsync(string id, T entity, CancellationToken ct = default);

    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    Task<long> CountAsync(
        Expression<Func<T, bool>> filter,
        CancellationToken ct = default);
}
