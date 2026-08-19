using System.Security.Claims;
using System.Text.Json;
using AttackShield.Api.Controllers;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using AttackShield.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AttackShield.Tests.Controllers;

/// <summary>
/// Covers the email-alert opt-in flowing through GET/PUT /api/settings, including
/// both the nested ("notifications.email") and flat ("emailNotifications") shapes
/// the frontend may send.
/// </summary>
public class SettingsControllerTests
{
    private readonly Mock<IUserRepository> _users = new();
    private const string ValidUserId = "507f1f77bcf86cd799439011";

    // Real fanout: SMTP stays disabled by default so nothing is sent and no cooldown runs.
    private readonly NotificationFanout _fanout = new(
        Mock.Of<IHttpClientFactory>(),
        Options.Create(new NotificationFanoutOptions()),
        NullLogger<NotificationFanout>.Instance);

    private SettingsController Sut(User? user, string? userId = ValidUserId)
    {
        _users.Setup(u => u.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var identity = userId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim("id", userId)], "test");

        return new SettingsController(_users.Object, _fanout)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private static User NewUser() => new() { Id = ValidUserId, Email = "operator@example.com" };

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement;

    private static object? Prop(IActionResult result, string name)
    {
        var body = result.Should().BeOfType<OkObjectResult>().Subject.Value;
        var data = body!.GetType().GetProperty("data")!.GetValue(body);
        return data!.GetType().GetProperty(name)?.GetValue(data);
    }

    [Fact]
    public async Task Get_NewUser_ReportsEmailAlertsOff()
    {
        var result = await Sut(NewUser()).Get(CancellationToken.None);

        Prop(result, "emailNotifications").Should().Be(false);
    }

    [Fact]
    public async Task Get_UnknownUser_Returns404()
    {
        var result = await Sut(null).Get(CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Subject.StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData("""{ "emailNotifications": true }""")]
    [InlineData("""{ "settings": { "emailNotifications": true } }""")]
    [InlineData("""{ "notifications": { "email": true } }""")]
    [InlineData("""{ "settings": { "notifications": { "email": true } } }""")]
    public async Task Update_EnablesEmailAlerts_AndPersists(string json)
    {
        var user = NewUser();

        var result = await Sut(user).Update(Body(json), CancellationToken.None);

        Prop(result, "emailNotifications").Should().Be(true);
        user.Settings.Notifications.Email.Should().BeTrue();
        _users.Verify(u => u.UpdateAsync(ValidUserId, user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_DisablesEmailAlerts()
    {
        var user = NewUser();
        user.Settings.Notifications.Email = true;

        var result = await Sut(user).Update(Body("""{ "emailNotifications": false }"""), CancellationToken.None);

        Prop(result, "emailNotifications").Should().Be(false);
        user.Settings.Notifications.Email.Should().BeFalse();
    }

    [Fact]
    public async Task Update_OtherSettings_LeavesEmailAlertsUntouched()
    {
        var user = NewUser();
        user.Settings.Notifications.Email = true;

        await Sut(user).Update(Body("""{ "soundEnabled": false }"""), CancellationToken.None);

        user.Settings.Notifications.Email.Should().BeTrue();
        user.Settings.Notifications.Sound.Should().BeFalse();
    }

    [Fact]
    public async Task Update_NonObjectBody_Returns400_AndDoesNotPersist()
    {
        var result = await Sut(NewUser()).Update(Body("\"nope\""), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Subject.StatusCode.Should().Be(400);
        _users.Verify(u => u.UpdateAsync(It.IsAny<string>(), It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Email cooldown ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EmailCooldown_UnknownUser_Returns404()
    {
        var result = await Sut(null).EmailCooldown(CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Subject.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task EmailCooldown_WhenSmtpDisabled_ReportsDisabledWithNoDeadline()
    {
        var user = NewUser();
        user.Settings.Notifications.Email = true;

        var result = await Sut(user).EmailCooldown(CancellationToken.None);

        Prop(result, "enabled").Should().Be(false);
        Prop(result, "nextAllowedAt").Should().BeNull();
    }

    [Fact]
    public async Task EmailCooldown_WhenUserOptedOut_ReportsDisabled()
    {
        var result = await Sut(NewUser()).EmailCooldown(CancellationToken.None);

        Prop(result, "enabled").Should().Be(false);
    }
}
