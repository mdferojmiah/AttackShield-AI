using AttackShield.Core.Entities;
using AttackShield.Infrastructure.Persistence.Repositories;
using FluentAssertions;

namespace AttackShield.Tests.Persistence;

[Collection("Mongo")]
public class AlertRepositoryTests
{
    private readonly MongoFixture _fx;
    public AlertRepositoryTests(MongoFixture fx) => _fx = fx;

    private static Alert Alert(string status, DateTime createdAt, string? assignedTo = null, DateTime? acceptedAt = null)
        => new()
        {
            Type = "high",
            Message = "m",
            Status = status,
            AssignedTo = assignedTo,
            AcceptedAt = acceptedAt,
            CreatedAt = createdAt,
        };

    [SkippableFact]
    public async Task GetNewAsync_ReturnsOnlyNew_NewestFirst()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new AlertRepository(_fx.NewContext());
        var now = DateTime.UtcNow;

        await repo.InsertAsync(Alert("new", now.AddMinutes(-2)));
        await repo.InsertAsync(Alert("new", now.AddMinutes(-1)));   // newest
        await repo.InsertAsync(Alert("accepted", now));            // excluded

        var result = await repo.GetNewAsync();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(a => a.Status == "new");
        result.Should().BeInDescendingOrder(a => a.CreatedAt);
    }

    [SkippableFact]
    public async Task GetActiveForAuthorityAsync_ReturnsAcceptedForThatAuthority_ByAcceptedAtDesc()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new AlertRepository(_fx.NewContext());
        var authority = "507f1f77bcf86cd799439011";
        var other = "507f1f77bcf86cd799439012";
        var now = DateTime.UtcNow;

        await repo.InsertAsync(Alert("accepted", now, authority, acceptedAt: now.AddMinutes(-5)));
        await repo.InsertAsync(Alert("accepted", now, authority, acceptedAt: now.AddMinutes(-1))); // newest accepted
        await repo.InsertAsync(Alert("accepted", now, other, acceptedAt: now));                   // other authority
        await repo.InsertAsync(Alert("resolved", now, authority, acceptedAt: now));               // wrong status

        var result = await repo.GetActiveForAuthorityAsync(authority);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(a => a.AssignedTo == authority && a.Status == "accepted");
        result.Should().BeInDescendingOrder(a => a.AcceptedAt);
    }

    [SkippableFact]
    public async Task GetHistoryForAuthorityAsync_IncludesAcceptedDismissedResolved_ExcludesNew()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new AlertRepository(_fx.NewContext());
        var authority = "507f1f77bcf86cd799439011";
        var now = DateTime.UtcNow;

        await repo.InsertAsync(Alert("accepted", now.AddMinutes(-3), authority));
        await repo.InsertAsync(Alert("dismissed", now.AddMinutes(-2), authority));
        await repo.InsertAsync(Alert("resolved", now.AddMinutes(-1), authority));
        await repo.InsertAsync(Alert("new", now, authority));                    // excluded (still unworked)

        var result = await repo.GetHistoryForAuthorityAsync(authority);

        result.Should().HaveCount(3);
        result.Should().OnlyContain(a => a.Status == "accepted" || a.Status == "dismissed" || a.Status == "resolved");
        result.Should().BeInDescendingOrder(a => a.CreatedAt);
    }

    [SkippableFact]
    public async Task GetHistoryForAuthorityAsync_ScopedToAuthority()
    {
        Skip.IfNot(_fx.Available, _fx.SkipReason);
        var repo = new AlertRepository(_fx.NewContext());
        var now = DateTime.UtcNow;

        await repo.InsertAsync(Alert("resolved", now, "507f1f77bcf86cd799439011"));
        await repo.InsertAsync(Alert("resolved", now, "507f1f77bcf86cd799439012"));

        var result = await repo.GetHistoryForAuthorityAsync("507f1f77bcf86cd799439011");

        result.Should().ContainSingle();
    }
}
