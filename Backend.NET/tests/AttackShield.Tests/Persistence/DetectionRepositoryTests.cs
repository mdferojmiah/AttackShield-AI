using AttackShield.Core.Entities;
using AttackShield.Infrastructure.Persistence.Repositories;
using FluentAssertions;

namespace AttackShield.Tests.Persistence;

[Collection("Mongo")]
public class DetectionRepositoryTests
{
    private readonly MongoFixture _fx;
    public DetectionRepositoryTests(MongoFixture fx) => _fx = fx;

    private static Detection Det(string weapon, string location, DateTime createdAt, string type = "weapon")
        => new()
        {
            WeaponType = weapon,
            Location = location,
            DetectionType = type,
            Confidence = 0.9,
            CreatedAt = createdAt,
        };

    [SkippableFact]
    public async Task FindRecentAsync_ReturnsMatch_WithinWindow()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new DetectionRepository(_fx.NewContext());
        await repo.InsertAsync(Det("pistol", "Lobby", DateTime.UtcNow.AddSeconds(-5)));

        var found = await repo.FindRecentAsync("pistol", "Lobby", DateTime.UtcNow.AddSeconds(-10));

        found.Should().NotBeNull();
        found!.WeaponType.Should().Be("pistol");
    }

    [SkippableFact]
    public async Task FindRecentAsync_ExcludesMatch_OutsideWindow()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new DetectionRepository(_fx.NewContext());
        // Older than the 10s window start.
        await repo.InsertAsync(Det("pistol", "Lobby", DateTime.UtcNow.AddSeconds(-30)));

        var found = await repo.FindRecentAsync("pistol", "Lobby", DateTime.UtcNow.AddSeconds(-10));

        found.Should().BeNull();
    }

    [SkippableFact]
    public async Task FindRecentAsync_RespectsWeaponTypeAndLocation()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new DetectionRepository(_fx.NewContext());
        var now = DateTime.UtcNow;
        await repo.InsertAsync(Det("pistol", "Lobby", now));

        (await repo.FindRecentAsync("knife", "Lobby", now.AddSeconds(-10))).Should().BeNull();
        (await repo.FindRecentAsync("pistol", "Garage", now.AddSeconds(-10))).Should().BeNull();
    }

    [SkippableFact]
    public async Task FindRecentAsync_WithDetectionTypeFilter_OnlyMatchesThatType()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new DetectionRepository(_fx.NewContext());
        var now = DateTime.UtcNow;
        await repo.InsertAsync(Det("face-a", "Lobby", now, type: "weapon"));

        // Same weapon+location+window, but the "face" filter excludes the stored "weapon" doc.
        (await repo.FindRecentAsync("face-a", "Lobby", now.AddSeconds(-30), "face")).Should().BeNull();

        await repo.InsertAsync(Det("face-a", "Lobby", now, type: "face"));
        (await repo.FindRecentAsync("face-a", "Lobby", now.AddSeconds(-30), "face")).Should().NotBeNull();
    }

    [SkippableFact]
    public async Task CountByTypeSinceAsync_CountsOnlyMatchingTypeWithinWindow()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new DetectionRepository(_fx.NewContext());
        var now = DateTime.UtcNow;

        await repo.InsertAsync(Det("pistol", "L", now.AddSeconds(-5), "weapon"));
        await repo.InsertAsync(Det("pistol", "L", now.AddSeconds(-8), "weapon"));
        await repo.InsertAsync(Det("pistol", "L", now.AddHours(-2), "weapon"));   // outside window
        await repo.InsertAsync(Det("loiter", "L", now.AddSeconds(-5), "suspicious_activity")); // wrong type

        var count = await repo.CountByTypeSinceAsync("weapon", now.AddMinutes(-1));

        count.Should().Be(2);
    }
}
