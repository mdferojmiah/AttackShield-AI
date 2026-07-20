using AttackShield.Infrastructure.Persistence;
using EphemeralMongo;
using Microsoft.Extensions.Options;

namespace AttackShield.Tests.Persistence;

/// <summary>
/// Boots a single ephemeral <c>mongod</c> for the whole repository test suite
/// (EphemeralMongo ships the binary, so no external server is required). Each
/// test uses a freshly-named database so collections never bleed across tests.
///
/// If the mongod binary cannot start in this environment, <see cref="Available"/>
/// is false and <see cref="SkipReason"/> explains why — the live-Mongo tests are
/// then skipped rather than reported as failures (see MongoAvailableFactAttribute).
/// </summary>
public sealed class MongoFixture : IDisposable
{
    private readonly IMongoRunner? _runner;

    public bool Available { get; }
    public string? SkipReason { get; }
    public string ConnectionString { get; } = string.Empty;

    public MongoFixture()
    {
        try
        {
            _runner = MongoRunner.Run(new MongoRunnerOptions
            {
                // Standalone node; keep logs quiet.
                AdditionalArguments = new[] { "--quiet" },
            });
            ConnectionString = _runner.ConnectionString;
            Available = true;
        }
        catch (Exception ex)
        {
            Available = false;
            SkipReason = $"Ephemeral mongod could not start: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>A MongoContext bound to a uniquely-named database on the ephemeral server.</summary>
    public MongoContext NewContext()
    {
        var opts = new MongoOptions
        {
            ConnectionString = ConnectionString,
            Database = "test_" + Guid.NewGuid().ToString("N"),
        };
        return new MongoContext(Options.Create(opts));
    }

    public void Dispose() => _runner?.Dispose();
}

[CollectionDefinition("Mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoFixture> { }
