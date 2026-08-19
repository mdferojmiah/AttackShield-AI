using System.Collections;
using System.Security.Claims;
using AttackShield.Api.Controllers;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace AttackShield.Tests.Controllers;

/// <summary>
/// Covers the paged GET /api/notifications contract: page-size clamping, the opaque cursor
/// round-trip, and rejection of malformed cursors. The repository is mocked; nothing touches Mongo.
/// </summary>
public class NotificationsControllerTests
{
    private readonly Mock<INotificationRepository> _notifications = new();
    private const string ValidUserId = "507f1f77bcf86cd799439011";
    private const string ValidNotificationId = "607f1f77bcf86cd799439099";

    private NotificationsController Sut(string role = "user", string? userId = ValidUserId)
    {
        var claims = new List<Claim> { new("role", role) };
        if (userId is not null) claims.Add(new Claim("id", userId));

        return new NotificationsController(_notifications.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
                },
            },
        };
    }

    private static Notification Note(string id, DateTime createdAt)
        => new() { Id = id, Type = "weapon", Title = "Weapon detected", CreatedAt = createdAt };

    /// <summary>Makes the repository return <paramref name="count"/> rows and the given HasMore.</summary>
    private void SetupPage(int count, bool hasMore)
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, count)
            .Select(i => Note(ObjectIdAt(i), start.AddMinutes(-i)))
            .ToList();

        _notifications
            .Setup(r => r.GetPageAsync(
                It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationPage(rows, hasMore));
    }

    private static string ObjectIdAt(int i) => ValidNotificationId[..^2] + i.ToString("x2");

    private static object? Prop(IActionResult result, string name)
    {
        var value = result.Should().BeOfType<OkObjectResult>().Subject.Value;
        return value!.GetType().GetProperty(name)?.GetValue(value);
    }

    private static int ItemCount(IActionResult result)
        => ((IEnumerable)Prop(result, "items")!).Cast<object>().Count();

    // ── Page size ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_NoLimit_UsesDefaultPageSize()
    {
        SetupPage(10, hasMore: true);

        await Sut().GetAll(null, null, CancellationToken.None);

        _notifications.Verify(r => r.GetPageAsync(
            ValidUserId, null, null, NotificationsController.DefaultPageSize, It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5, 5)]
    [InlineData(500, NotificationsController.MaxPageSize)]
    public async Task GetAll_ClampsLimitToSupportedRange(int requested, int expected)
    {
        SetupPage(1, hasMore: false);

        await Sut().GetAll(requested, null, CancellationToken.None);

        _notifications.Verify(r => r.GetPageAsync(
            ValidUserId, null, null, expected, It.IsAny<CancellationToken>()));
    }

    // ── Scoping ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_AsAdmin_QueriesAcrossAllUsers()
    {
        SetupPage(1, hasMore: false);

        await Sut(role: "admin").GetAll(null, null, CancellationToken.None);

        _notifications.Verify(r => r.GetPageAsync(
            null, null, null, It.IsAny<int>(), It.IsAny<CancellationToken>()));
    }

    // ── Cursor ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WhenMorePagesExist_ReturnsCursorAndHasMore()
    {
        SetupPage(10, hasMore: true);

        var result = await Sut().GetAll(null, null, CancellationToken.None);

        ItemCount(result).Should().Be(10);
        Prop(result, "hasMore").Should().Be(true);
        Prop(result, "nextCursor").Should().NotBeNull();
    }

    [Fact]
    public async Task GetAll_OnLastPage_OmitsCursor()
    {
        SetupPage(4, hasMore: false);

        var result = await Sut().GetAll(null, null, CancellationToken.None);

        Prop(result, "hasMore").Should().Be(false);
        Prop(result, "nextCursor").Should().BeNull("a client must not request a page past the end");
    }

    [Fact]
    public async Task GetAll_EmptyFeed_ReturnsEmptyItemsAndNoCursor()
    {
        SetupPage(0, hasMore: false);

        var result = await Sut().GetAll(null, null, CancellationToken.None);

        ItemCount(result).Should().Be(0);
        Prop(result, "nextCursor").Should().BeNull();
    }

    [Fact]
    public async Task GetAll_ReturnedCursor_ResolvesToLastRowOfPreviousPage()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        SetupPage(10, hasMore: true);

        var first = await Sut().GetAll(null, null, CancellationToken.None);
        var cursor = (string)Prop(first, "nextCursor")!;

        await Sut().GetAll(null, cursor, CancellationToken.None);

        // Row index 9 is the tail of a 10-row page seeded one minute apart.
        _notifications.Verify(r => r.GetPageAsync(
            ValidUserId, start.AddMinutes(-9), ObjectIdAt(9),
            NotificationsController.DefaultPageSize, It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData("not-base64!!")]
    [InlineData("bm90LWEtY3Vyc29y")]                       // "not-a-cursor" — no separator
    [InlineData("fHNvbWVpZA==")]                           // "|someid" — empty ticks
    [InlineData("MTIzNHw=")]                               // "1234|" — empty id
    [InlineData("MTIzNHxub3QtYW4tb2JqZWN0aWQ=")]           // "1234|not-an-objectid"
    [InlineData("OTk5OTk5OTk5OTk5OTk5OTk5OXxhYmM=")]       // ticks overflow
    public async Task GetAll_MalformedCursor_ReturnsBadRequestAndNeverQueries(string cursor)
    {
        var result = await Sut().GetAll(null, cursor, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(400);
        _notifications.Verify(r => r.GetPageAsync(
            It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Payload shape ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_OmitsImageUrl_SoAPageDoesNotCarryBase64Snapshots()
    {
        var row = Note(ValidNotificationId, DateTime.UtcNow);
        row.ImageUrl = "data:image/jpeg;base64,AAAA";
        _notifications
            .Setup(r => r.GetPageAsync(
                It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NotificationPage(new[] { row }, false));

        var result = await Sut().GetAll(null, null, CancellationToken.None);

        var item = ((IEnumerable)Prop(result, "items")!).Cast<object>().Single();
        item.GetType().GetProperty("ImageUrl").Should().BeNull();
        item.GetType().GetProperty("Title").Should().NotBeNull("summaries still need display fields");
    }

    [Fact]
    public async Task Get_ById_IncludesImageUrl_SoTheDetailsViewCanRenderTheSnapshot()
    {
        var row = Note(ValidNotificationId, DateTime.UtcNow);
        row.ImageUrl = "data:image/jpeg;base64,AAAA";
        row.UserId = ValidUserId;
        _notifications
            .Setup(r => r.GetByIdAsync(ValidNotificationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

        var result = await Sut().Get(ValidNotificationId, CancellationToken.None);

        var data = Prop(result, "data")!;
        data.GetType().GetProperty("ImageUrl")!.GetValue(data)
            .Should().Be("data:image/jpeg;base64,AAAA");
    }
}
