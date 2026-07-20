using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using MongoDB.Driver;

namespace AttackShield.Infrastructure.Persistence.Repositories;

public sealed class AuthorityRepository : MongoRepository<Authority>, IAuthorityRepository
{
    public AuthorityRepository(MongoContext ctx) : base(ctx.Authorities) { }

    public Task<Authority?> GetByEmailAsync(string email, CancellationToken ct = default)
        => Collection.Find(a => a.Email == email.ToLowerInvariant()).FirstOrDefaultAsync(ct)!;

    public Task<Authority?> GetByEmailOrOfficerIdAsync(string email, string officerId, CancellationToken ct = default)
    {
        var filter = Builders<Authority>.Filter.Or(
            Builders<Authority>.Filter.Eq(a => a.Email, email.ToLowerInvariant()),
            Builders<Authority>.Filter.Eq(a => a.OfficerId, officerId.ToUpperInvariant()));
        return Collection.Find(filter).FirstOrDefaultAsync(ct)!;
    }
}
