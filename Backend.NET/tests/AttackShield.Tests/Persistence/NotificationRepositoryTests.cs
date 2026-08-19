using AttackShield.Core.Entities;
using AttackShield.Infrastructure.Persistence.Repositories;
using FluentAssertions;

namespace AttackShield.Tests.Persistence;

/// <summary>
/// Covers the newest-first keyset paging that backs "see more" on the notifications feed.
/// </summary>
[Collection("Mongo")]
public class NotificationRepositoryTests
{
    private readonly MongoFixture _fx;
    public NotificationRepositoryTests(MongoFixture fx) => _fx = fx;

    private static Notification Note(string title, DateTime createdAt, string? userId = null)
        => new()
        {
            Type = "weapon",
            Title = title,
            Description = title,
            CreatedAt = createdAt,
            UserId = userId,
        };

    /// <summary>Seeds <paramref name="count"/> notifications, newest first as "n0".</summary>
    private static async Task SeedAsync(NotificationRepository repo, int count, string? userId = null)
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < count; i++)
            await repo.InsertAsync(Note($"n{i}", start.AddMinutes(-i), userId));
    }

    [SkippableFact]
    public async Task GetPageAsync_ReturnsNewestFirst_LimitedToPageSize()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new NotificationRepository(_fx.NewContext());
        await SeedAsync(repo, 25);

        var page = await repo.GetPageAsync(null, null, null, 10);

        page.Items.Should().HaveCount(10);
        page.Items.Select(n => n.Title).Should().ContainInOrder("n0", "n1", "n2");
        page.HasMore.Should().BeTrue();
    }

    [SkippableFact]
    public async Task GetPageAsync_WalksEntireFeed_WithoutGapsOrDuplicates()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new NotificationRepository(_fx.NewContext());
        await SeedAsync(repo, 25);

        var seen = new List<string>();
        DateTime? before = null;
        string? beforeId = null;

        while (true)
        {
            var page = await repo.GetPageAsync(null, before, beforeId, 10);
            seen.AddRange(page.Items.Select(n => n.Title));
            if (!page.HasMore) break;

            var last = page.Items[^1];
            before = last.CreatedAt;
            beforeId = last.Id;
        }

        seen.Should().HaveCount(25).And.OnlyHaveUniqueItems();
        seen.Should().BeEquivalentTo(Enumerable.Range(0, 25).Select(i => $"n{i}"));
    }

    [SkippableFact]
    public async Task GetPageAsync_ExactMultipleOfPageSize_ReportsNoMoreOnLastPage()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new NotificationRepository(_fx.NewContext());
        await SeedAsync(repo, 20);

        var first = await repo.GetPageAsync(null, null, null, 10);
        first.HasMore.Should().BeTrue();

        var last = first.Items[^1];
        var second = await repo.GetPageAsync(null, last.CreatedAt, last.Id, 10);

        second.Items.Should().HaveCount(10);
        second.HasMore.Should().BeFalse("the 20th row is the last, so no extra row is fetched");
    }

    [SkippableFact]
    public async Task GetPageAsync_SameTimestampRows_AreNotSkippedAcrossPages()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new NotificationRepository(_fx.NewContext());
        // A detection burst writes several notifications on the same instant.
        var burst = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 6; i++)
            await repo.InsertAsync(Note($"burst{i}", burst));

        var first = await repo.GetPageAsync(null, null, null, 3);
        var last = first.Items[^1];
        var second = await repo.GetPageAsync(null, last.CreatedAt, last.Id, 3);

        var seen = first.Items.Concat(second.Items).Select(n => n.Title).ToList();
        seen.Should().HaveCount(6).And.OnlyHaveUniqueItems();
    }

    [SkippableFact]
    public async Task GetPageAsync_ScopesToUser_WhenUserIdSupplied()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new NotificationRepository(_fx.NewContext());
        var mine = "507f1f77bcf86cd799439011";
        var theirs = "507f1f77bcf86cd799439012";
        await SeedAsync(repo, 3, mine);
        await SeedAsync(repo, 4, theirs);

        var scoped = await repo.GetPageAsync(mine, null, null, 10);
        var all = await repo.GetPageAsync(null, null, null, 10);

        scoped.Items.Should().HaveCount(3);
        scoped.Items.Should().OnlyContain(n => n.UserId == mine);
        all.Items.Should().HaveCount(7, "a null userId is the admin view across all users");
    }

    [SkippableFact]
    public async Task GetPageAsync_EmptyCollection_ReturnsEmptyPageWithNoMore()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new NotificationRepository(_fx.NewContext());

        var page = await repo.GetPageAsync(null, null, null, 10);

        page.Items.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
    }
}
