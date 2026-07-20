using AttackShield.Core.Entities;
using AttackShield.Infrastructure.Persistence.Repositories;
using FluentAssertions;

namespace AttackShield.Tests.Persistence;

[Collection("Mongo")]
public class UserRepositoryTests
{
    private readonly MongoFixture _fx;
    public UserRepositoryTests(MongoFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task GetByEmailAsync_MatchesStoredLowercaseEmail_CaseInsensitiveInput()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var ctx = _fx.NewContext();
        var repo = new UserRepository(ctx);

        // Stored lowercase (the way signup persists it).
        await repo.InsertAsync(new User { Name = "Alice", Email = "alice@example.com", Password = "x" });

        // Uppercase query input is lowercased by the repo before matching.
        var found = await repo.GetByEmailAsync("ALICE@EXAMPLE.COM");

        found.Should().NotBeNull();
        found!.Name.Should().Be("Alice");
    }

    [SkippableFact]
    public async Task GetByEmailAsync_ReturnsNull_WhenNoMatch()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new UserRepository(_fx.NewContext());

        (await repo.GetByEmailAsync("nobody@example.com")).Should().BeNull();
    }

    [SkippableFact]
    public async Task TouchLastLoginAsync_UpdatesLastLoginAndUpdatedAt()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new UserRepository(_fx.NewContext());

        var user = new User
        {
            Name = "Bob",
            Email = "bob@example.com",
            Password = "x",
            UpdatedAt = DateTime.UtcNow.AddDays(-10),
        };
        await repo.InsertAsync(user);

        var when = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await repo.TouchLastLoginAsync(user.Id!, when);

        var reloaded = await repo.GetByIdAsync(user.Id!);
        reloaded!.LastLogin.Should().BeCloseTo(when, TimeSpan.FromMilliseconds(999));
        reloaded.UpdatedAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }
}
