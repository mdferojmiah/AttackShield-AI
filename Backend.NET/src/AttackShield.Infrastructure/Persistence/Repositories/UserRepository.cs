using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using MongoDB.Driver;

namespace AttackShield.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : MongoRepository<User>, IUserRepository
{
    public UserRepository(MongoContext ctx) : base(ctx.Users) { }

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
        => Collection.Find(u => u.Email == email.ToLowerInvariant()).FirstOrDefaultAsync(ct)!;

    public Task<User?> GetByGoogleIdAsync(string googleId, CancellationToken ct = default)
        => Collection.Find(u => u.GoogleId == googleId).FirstOrDefaultAsync(ct)!;

    public async Task TouchLastLoginAsync(string id, DateTime when, CancellationToken ct = default)
    {
        var update = Builders<User>.Update
            .Set(u => u.LastLogin, when)
            .Set(u => u.UpdatedAt, DateTime.UtcNow);
        await Collection.UpdateOneAsync(u => u.Id == id, update, cancellationToken: ct);
    }
}
