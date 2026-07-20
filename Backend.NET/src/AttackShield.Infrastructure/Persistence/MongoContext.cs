using AttackShield.Core.Entities;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace AttackShield.Infrastructure.Persistence;

/// <summary>
/// Owns the <see cref="IMongoDatabase"/> connection and exposes strongly-typed
/// collections. Collection names match the original Mongoose models (lower-case
/// pluralised), so this API reads/writes the exact same documents.
/// </summary>
public sealed class MongoContext
{
    private readonly IMongoDatabase _db;

    // Guards one-time global convention/index registration.
    private static int _initialised;

    public MongoContext(IOptions<MongoOptions> options)
    {
        var opts = options.Value;
        RegisterConventions();

        var client = new MongoClient(opts.ConnectionString);
        _db = client.GetDatabase(opts.Database);

        // Fire-and-forget index creation; safe to run repeatedly.
        _ = EnsureIndexesAsync();
    }

    // Mongoose pluralises: User -> "users", Authority -> "authorities", etc.
    public IMongoCollection<User> Users => _db.GetCollection<User>("users");
    public IMongoCollection<Authority> Authorities => _db.GetCollection<Authority>("authorities");
    public IMongoCollection<Detection> Detections => _db.GetCollection<Detection>("detections");
    public IMongoCollection<Alert> Alerts => _db.GetCollection<Alert>("alerts");
    public IMongoCollection<Notification> Notifications => _db.GetCollection<Notification>("notifications");
    public IMongoCollection<Stats> Stats => _db.GetCollection<Stats>("stats");

    public IMongoCollection<T> GetCollection<T>(string name) => _db.GetCollection<T>(name);

    private static void RegisterConventions()
    {
        if (Interlocked.Exchange(ref _initialised, 1) == 1)
            return;

        // Ignore extra fields (e.g. Mongoose's __v) instead of throwing, and
        // omit null/defaulted members we've flagged, keeping documents lean.
        var pack = new ConventionPack
        {
            new IgnoreExtraElementsConvention(true),
        };
        ConventionRegistry.Register("attackshield-conventions", pack, _ => true);
    }

    private async Task EnsureIndexesAsync()
    {
        try
        {
            await Users.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.Email),
                    new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.GoogleId),
                    new CreateIndexOptions { Unique = true, Sparse = true }),
                new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Descending(u => u.CreatedAt)),
            });

            await Authorities.Indexes.CreateManyAsync(new[]
            {
                new CreateIndexModel<Authority>(
                    Builders<Authority>.IndexKeys.Ascending(a => a.Email),
                    new CreateIndexOptions { Unique = true }),
                new CreateIndexModel<Authority>(
                    Builders<Authority>.IndexKeys.Ascending(a => a.OfficerId),
                    new CreateIndexOptions { Unique = true }),
            });

            // Detections are queried by (type, location, createdAt) for de-dup.
            await Detections.Indexes.CreateOneAsync(new CreateIndexModel<Detection>(
                Builders<Detection>.IndexKeys
                    .Ascending(d => d.WeaponType)
                    .Ascending(d => d.Location)
                    .Descending(d => d.CreatedAt)));

            await Alerts.Indexes.CreateOneAsync(new CreateIndexModel<Alert>(
                Builders<Alert>.IndexKeys.Ascending(a => a.Status).Descending(a => a.CreatedAt)));
        }
        catch
        {
            // Index creation is best-effort; a running server without indexes
            // still functions. Real failures surface on first query.
        }
    }
}
