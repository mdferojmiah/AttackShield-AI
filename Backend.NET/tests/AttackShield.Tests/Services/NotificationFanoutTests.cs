using AttackShield.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AttackShield.Tests.Services;

/// <summary>
/// Covers the gating and body-building logic. The SMTP handshake itself is not
/// exercised — Smtp.Enabled stays false unless a test needs the cooldown path.
/// </summary>
public class NotificationFanoutTests
{
    private const string Recipient = "operator@example.com";

    private static AlertNotification Payload(string type = "weapon") => new(
        Type: type,
        Title: "Weapon detected",
        Message: "A pistol was detected in the Lobby.",
        Location: "Lobby",
        Confidence: 0.87,
        CameraName: "Cam 1",
        ImageUrl: null,
        CreatedAt: new DateTime(2025, 3, 4, 15, 30, 0, DateTimeKind.Utc));

    private static NotificationFanout Sut(Action<NotificationFanoutOptions>? configure = null)
    {
        var options = new NotificationFanoutOptions();
        configure?.Invoke(options);
        return new NotificationFanout(
            Mock.Of<IHttpClientFactory>(),
            Options.Create(options),
            NullLogger<NotificationFanout>.Instance);
    }

    // ── Cooldown ────────────────────────────────────────────────────────────────

    [Fact]
    public void ClaimCooldown_FirstAlert_IsAllowed()
    {
        var claim = Sut(o => o.Smtp.CooldownMinutes = 5).ClaimCooldown(Recipient, "weapon");

        claim.Considered.Should().BeTrue();
        claim.Sent.Should().BeTrue();
        claim.CooldownMinutes.Should().Be(5);
        claim.NextAllowedAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ClaimCooldown_RepeatOfSameTypeWithinWindow_IsSuppressed()
    {
        var sut = Sut(o => o.Smtp.CooldownMinutes = 5);

        var first = sut.ClaimCooldown(Recipient, "weapon");
        var second = sut.ClaimCooldown(Recipient, "weapon");

        first.Sent.Should().BeTrue();
        second.Sent.Should().BeFalse();
        // The suppressed attempt must not extend the window opened by the first send.
        second.NextAllowedAt.Should().Be(first.NextAllowedAt);
    }

    [Fact]
    public void ClaimCooldown_DifferentAlertType_IsNotSuppressed()
    {
        var sut = Sut(o => o.Smtp.CooldownMinutes = 5);
        sut.ClaimCooldown(Recipient, "weapon");

        sut.ClaimCooldown(Recipient, "hit_list").Sent.Should().BeTrue();
    }

    [Fact]
    public void ClaimCooldown_DifferentRecipient_IsNotSuppressed()
    {
        var sut = Sut(o => o.Smtp.CooldownMinutes = 5);
        sut.ClaimCooldown(Recipient, "weapon");

        sut.ClaimCooldown("second@example.com", "weapon").Sent.Should().BeTrue();
    }

    [Fact]
    public void ClaimCooldown_ZeroMinutes_NeverSuppresses()
    {
        var sut = Sut(o => o.Smtp.CooldownMinutes = 0);

        sut.ClaimCooldown(Recipient, "weapon").Sent.Should().BeTrue();
        var second = sut.ClaimCooldown(Recipient, "weapon");

        second.Sent.Should().BeTrue();
        // Nothing to count down to when the throttle is off.
        second.NextAllowedAt.Should().BeNull();
    }

    // ── Peek (drives the navbar countdown) ──────────────────────────────────────

    [Fact]
    public void PeekCooldown_WithNoHistory_IsNull()
        => Sut(o => o.Smtp.CooldownMinutes = 5).PeekCooldown(Recipient).Should().BeNull();

    [Fact]
    public void PeekCooldown_AfterSend_ReportsTypeAndDeadline()
    {
        var sut = Sut(o => o.Smtp.CooldownMinutes = 5);
        var claim = sut.ClaimCooldown(Recipient, "weapon");

        var peek = sut.PeekCooldown(Recipient);

        peek.Should().NotBeNull();
        peek!.Value.Type.Should().Be("weapon");
        peek.Value.NextAllowedAt.Should().Be(claim.NextAllowedAt);
    }

    [Fact]
    public void PeekCooldown_DoesNotStartAWindow()
    {
        var sut = Sut(o => o.Smtp.CooldownMinutes = 5);

        sut.PeekCooldown(Recipient);

        // Polling must never consume the recipient's first slot.
        sut.ClaimCooldown(Recipient, "weapon").Sent.Should().BeTrue();
    }

    [Fact]
    public void PeekCooldown_WithMultipleTypes_ReportsTheLongestRemaining()
    {
        var sut = Sut(o => o.Smtp.CooldownMinutes = 5);
        sut.ClaimCooldown(Recipient, "weapon");
        var latest = sut.ClaimCooldown(Recipient, "hit_list");

        var peek = sut.PeekCooldown(Recipient);

        peek!.Value.Type.Should().Be("hit_list");
        peek.Value.NextAllowedAt.Should().Be(latest.NextAllowedAt);
    }

    [Fact]
    public void PeekCooldown_OtherRecipient_IsNotVisible()
    {
        var sut = Sut(o => o.Smtp.CooldownMinutes = 5);
        sut.ClaimCooldown(Recipient, "weapon");

        sut.PeekCooldown("second@example.com").Should().BeNull();
    }

    [Fact]
    public void PeekCooldown_ZeroMinutes_IsNull()
    {
        var sut = Sut(o => o.Smtp.CooldownMinutes = 0);
        sut.ClaimCooldown(Recipient, "weapon");

        sut.PeekCooldown(Recipient).Should().BeNull();
    }

    // ── Opt-in gate ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishAsync_WithoutRecipient_DoesNotConsumeCooldown(string? email)
    {
        var sut = Sut(o =>
        {
            o.Smtp.Enabled = true;
            o.Smtp.CooldownMinutes = 5;
        });

        var state = await sut.PublishAsync(Payload(), email);

        // Not considered, so the UI shows no countdown...
        state.Considered.Should().BeFalse();
        state.NextAllowedAt.Should().BeNull();
        // ...and a real recipient still gets the first slot.
        sut.ClaimCooldown(Recipient, "weapon").Sent.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_SmtpDisabled_DoesNotConsumeCooldown()
    {
        var sut = Sut(o =>
        {
            o.Smtp.Enabled = false;
            o.Smtp.CooldownMinutes = 5;
        });

        var state = await sut.PublishAsync(Payload(), Recipient);

        state.Considered.Should().BeFalse();
        sut.ClaimCooldown(Recipient, "weapon").Sent.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_WebhooksAndSmtpDisabled_DoesNotThrow()
    {
        var act = async () => await Sut().PublishAsync(Payload(), Recipient);

        await act.Should().NotThrowAsync();
    }

    // ── Subject ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildSubject_IncludesTitleAndLocation()
        => NotificationFanout.BuildSubject(Payload()).Should().Be("[AttackShield] Weapon detected \u2014 Lobby");

    [Fact]
    public void BuildSubject_WithoutLocation_OmitsSeparator()
    {
        var subject = NotificationFanout.BuildSubject(Payload() with { Location = null });

        subject.Should().Be("[AttackShield] Weapon detected");
    }

    // ── HTML body ───────────────────────────────────────────────────────────────

    [Fact]
    public void BuildHtmlBody_ContainsAlertDetails()
    {
        var html = NotificationFanout.BuildHtmlBody(Payload(), null);

        html.Should().Contain("Weapon detected")
            .And.Contain("Lobby")
            .And.Contain("Cam 1")
            .And.Contain("87%")
            .And.Contain("2025-03-04 15:30:00 UTC");
    }

    [Fact]
    public void BuildHtmlBody_EncodesUntrustedValues()
    {
        var payload = Payload() with { Location = "<script>alert(1)</script>" };

        var html = NotificationFanout.BuildHtmlBody(payload, null);

        html.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
    }

    [Fact]
    public void BuildHtmlBody_WithSnapshotContentId_ReferencesInlineImage()
    {
        var html = NotificationFanout.BuildHtmlBody(Payload(), "abc123");

        html.Should().Contain("src=\"cid:abc123\"");
    }

    [Fact]
    public void BuildHtmlBody_WithHostedImageUrl_LinksToSnapshot()
    {
        var payload = Payload() with { ImageUrl = "https://cdn.example.com/snap.jpg" };

        var html = NotificationFanout.BuildHtmlBody(payload, null);

        html.Should().Contain("https://cdn.example.com/snap.jpg");
    }

    [Fact]
    public void BuildHtmlBody_WithDataUriAndNoInlinePart_DoesNotEmbedTheDataUri()
    {
        var payload = Payload() with { ImageUrl = "data:image/jpeg;base64,AAAA" };

        var html = NotificationFanout.BuildHtmlBody(payload, null);

        html.Should().NotContain("data:image/jpeg");
    }

    // ── Snapshot part ───────────────────────────────────────────────────────────

    [Fact]
    public void TryCreateSnapshot_ValidDataUri_ReturnsLinkedResourceWithContentId()
    {
        // 1x1 transparent GIF.
        const string dataUri = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

        using var resource = NotificationFanout.TryCreateSnapshot(dataUri);

        resource.Should().NotBeNull();
        resource!.ContentId.Should().NotBeNullOrWhiteSpace();
        resource.ContentType.MediaType.Should().Be("image/gif");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://cdn.example.com/snap.jpg")]         // hosted URL, not a data URI
    [InlineData("data:text/plain;base64,aGVsbG8=")]          // not an image
    [InlineData("data:image/jpeg,notbase64")]                // missing ;base64
    [InlineData("data:image/jpeg;base64")]                   // missing comma
    [InlineData("data:image/jpeg;base64,%%%not-base64%%%")]  // undecodable
    [InlineData("data:image/jpeg;base64,")]                  // empty payload
    public void TryCreateSnapshot_UnusableInput_ReturnsNull(string? imageUrl)
        => NotificationFanout.TryCreateSnapshot(imageUrl).Should().BeNull();
}
