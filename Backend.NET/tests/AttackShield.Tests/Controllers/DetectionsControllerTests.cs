using AttackShield.Api.Controllers;
using AttackShield.Api.Hubs;
using AttackShield.Core.DTOs;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using AttackShield.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AttackShield.Tests.Controllers;

/// <summary>
/// Covers the risky detection-pipeline logic in POST /api/detections/receive:
/// the threshold gate, the per-type de-dup window, the face fast-path (skips
/// dedup), and the full weapon path (detection + notification + alert + 4 broadcasts).
/// Repositories and the broadcaster are mocked; nothing touches Mongo or SignalR.
/// </summary>
public class DetectionsControllerTests
{
    private readonly Mock<IDetectionRepository> _detections = new();
    private readonly Mock<INotificationRepository> _notifications = new();
    private readonly Mock<IAlertRepository> _alerts = new();
    private readonly Mock<IDetectionBroadcaster> _broadcaster = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly NotificationFanout _fanout = new(
        Mock.Of<IHttpClientFactory>(),
        Microsoft.Extensions.Options.Options.Create(new NotificationFanoutOptions()),
        NullLogger<NotificationFanout>.Instance);

    private const string ValidUserId = "507f1f77bcf86cd799439011";

    private DetectionsController Sut() => new(
        _detections.Object, _notifications.Object, _alerts.Object,
        _users.Object, _broadcaster.Object, _fanout, NullLogger<DetectionsController>.Instance);

    private static ReceiveDetectionRequest Req(
        string? weaponType = "pistol",
        string? location = "Lobby",
        double? confidence = 0.9,
        string? detectionType = null,
        string? userId = null,
        string? cameraName = null,
        string? cameraNameSnake = null)
        => new(
            WeaponType: weaponType,
            Location: location,
            Confidence: confidence,
            ImageUrl: "http://img/1.jpg",
            UserId: userId,
            CameraName: cameraName,
            Camera_Name: cameraNameSnake,
            DetectionType: detectionType,
            CameraId: "cam-1",
            Bbox: null);

    // Reads a property off the anonymous { success, ... } response body via reflection.
    private static object? Prop(IActionResult result, string name)
    {
        var value = result.Should().BeOfType<OkObjectResult>().Subject.Value;
        return value!.GetType().GetProperty(name)?.GetValue(value);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "Lobby", 0.9)]
    [InlineData("pistol", null, 0.9)]
    [InlineData("pistol", "Lobby", null)]
    public async Task Receive_MissingRequiredFields_ReturnsFail(string? weapon, string? location, double? confidence)
    {
        var result = await Sut().Receive(Req(weapon, location, confidence), CancellationToken.None);

        var obj = result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(400);
        _detections.Verify(d => d.InsertAsync(It.IsAny<Detection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Threshold gate ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Receive_BelowThreshold_ReturnsEarly_NoPersistNoBroadcast()
    {
        var result = await Sut().Receive(Req(confidence: 0.59), CancellationToken.None);

        Prop(result, "message").Should().Be("Detection below threshold");
        _detections.Verify(d => d.InsertAsync(It.IsAny<Detection>(), It.IsAny<CancellationToken>()), Times.Never);
        _broadcaster.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Receive_WeaponAtThreshold_ContinuesToWeaponPath()
    {
        _detections
            .Setup(d => d.FindRecentAsync("pistol", "Lobby", It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Detection { DetectionType = "weapon" });

        var result = await Sut().Receive(Req(confidence: 0.60), CancellationToken.None);

        Prop(result, "message").Should().Be("Duplicate ignored");
        _detections.Verify(d => d.FindRecentAsync(
            "pistol", "Lobby", It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Receive_FaceBelowFaceThreshold_ReturnsEarly()
    {
        // face threshold 0.35 — a 0.30 face is below it even though it clears the weapon 0.20 gate.
        var result = await Sut().Receive(Req(confidence: 0.30, detectionType: "face"), CancellationToken.None);

        Prop(result, "message").Should().Be("Detection below threshold");
        _detections.Verify(d => d.InsertAsync(It.IsAny<Detection>(), It.IsAny<CancellationToken>()), Times.Never);
        _broadcaster.VerifyNoOtherCalls();
    }

    // ── Face fast-path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Receive_Face_SkipsDedupGate_AlwaysBroadcastsOverlay_AndSavesWhenNoRecent()
    {
        _detections
            .Setup(d => d.FindRecentAsync("known-face", "Lobby", It.IsAny<DateTime>(), "face", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Detection?)null);

        var result = await Sut().Receive(
            Req(weaponType: "known-face", confidence: 0.9, detectionType: "face"), CancellationToken.None);

        Prop(result, "success").Should().Be(true);
        // Face queries FindRecentAsync only with the "face" detectionType overload (the dedup gate uses the null overload).
        _detections.Verify(d => d.FindRecentAsync("known-face", "Lobby", It.IsAny<DateTime>(), "face", null, It.IsAny<CancellationToken>()), Times.Once);
        _detections.Verify(d => d.FindRecentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _detections.Verify(d => d.InsertAsync(It.Is<Detection>(x => x.DetectionType == "face"), It.IsAny<CancellationToken>()), Times.Once);
        _broadcaster.Verify(b => b.DetectionOverlayAsync(It.IsAny<object>(), null), Times.Once);
        // No weapon/notification/alert side-effects for a face.
        _notifications.Verify(n => n.InsertAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Never);
        _alerts.Verify(a => a.InsertAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
        _broadcaster.Verify(b => b.WeaponDetectedAsync(It.IsAny<object>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Receive_Face_WithinSaveWindow_BroadcastsOverlayButDoesNotInsert()
    {
        _detections
            .Setup(d => d.FindRecentAsync("known-face", "Lobby", It.IsAny<DateTime>(), "face", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Detection { DetectionType = "face" });

        var result = await Sut().Receive(
            Req(weaponType: "known-face", confidence: 0.9, detectionType: "face"), CancellationToken.None);

        Prop(result, "success").Should().Be(true);
        _detections.Verify(d => d.InsertAsync(It.IsAny<Detection>(), It.IsAny<CancellationToken>()), Times.Never);
        _broadcaster.Verify(b => b.DetectionOverlayAsync(It.IsAny<object>(), null), Times.Once);
    }

    // ── De-dup gate (weapon / suspicious_activity) ──────────────────────────────

    [Fact]
    public async Task Receive_Weapon_DuplicateWithinWindow_Ignored_NoPersistNoBroadcast()
    {
        _detections
            .Setup(d => d.FindRecentAsync("pistol", "Lobby", It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Detection { DetectionType = "weapon" });

        var result = await Sut().Receive(Req(), CancellationToken.None);

        Prop(result, "message").Should().Be("Duplicate ignored");
        _detections.Verify(d => d.InsertAsync(It.IsAny<Detection>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifications.Verify(n => n.InsertAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Never);
        _alerts.Verify(a => a.InsertAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()), Times.Never);
        _broadcaster.VerifyNoOtherCalls();
    }

    // ── Weapon full path ────────────────────────────────────────────────────────

    [Fact]
    public async Task Receive_Weapon_NoDuplicate_PersistsDetectionNotificationAlert_AndBroadcastsAll()
    {
        _detections
            .Setup(d => d.FindRecentAsync("pistol", "Lobby", It.IsAny<DateTime>(), null, ValidUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Detection?)null);
        _detections
            .Setup(d => d.InsertAsync(It.IsAny<Detection>(), It.IsAny<CancellationToken>()))
            .Callback<Detection, CancellationToken>((d, _) => d.Id = "det-1")
            .Returns(Task.CompletedTask);
        _notifications
            .Setup(n => n.InsertAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, _) => n.Id = "notif-1")
            .Returns(Task.CompletedTask);
        _alerts
            .Setup(a => a.InsertAsync(It.IsAny<Alert>(), It.IsAny<CancellationToken>()))
            .Callback<Alert, CancellationToken>((a, _) => a.Id = "alert-1")
            .Returns(Task.CompletedTask);

        var result = await Sut().Receive(Req(userId: ValidUserId, cameraName: "Cam-A"), CancellationToken.None);

        Prop(result, "success").Should().Be(true);
        Prop(result, "detection").Should().Be("det-1");
        Prop(result, "notification").Should().Be("notif-1");
        Prop(result, "alert").Should().Be("alert-1");

        _detections.Verify(d => d.InsertAsync(It.Is<Detection>(x =>
            x.DetectionType == "weapon" && x.WeaponType == "pistol" && x.UserId == ValidUserId), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.InsertAsync(It.Is<Notification>(x =>
            x.Type == "weapon" && x.ImageUrl == "http://img/1.jpg"), It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.InsertAsync(It.Is<Alert>(x =>
            x.Status == "new" && x.Type == "high" && x.DetectionId == "det-1" && x.ImageUrl == "http://img/1.jpg"), It.IsAny<CancellationToken>()), Times.Once);

        _broadcaster.Verify(b => b.WeaponDetectedAsync(It.IsAny<object>(), ValidUserId), Times.Once);
        _broadcaster.Verify(b => b.NotificationCreatedAsync(It.IsAny<object>(), ValidUserId), Times.Once);
        _broadcaster.Verify(b => b.AlertCreatedAsync(It.IsAny<object>(), ValidUserId), Times.Once);
        _broadcaster.Verify(b => b.DetectionOverlayAsync(It.IsAny<object>(), ValidUserId), Times.Once);
    }

    [Fact]
    public async Task Receive_Weapon_InvalidUserId_StoredAsNull()
    {
        _detections
            .Setup(d => d.FindRecentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Detection?)null);

        await Sut().Receive(Req(userId: "not-an-objectid"), CancellationToken.None);

        _detections.Verify(d => d.InsertAsync(It.Is<Detection>(x => x.UserId == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Suspicious activity path ────────────────────────────────────────────────

    [Fact]
    public async Task Receive_SuspiciousActivity_PersistsNotificationAndAlert()
    {
        _detections
            .Setup(d => d.FindRecentAsync("loitering", "Lobby", It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Detection?)null);

        var result = await Sut().Receive(
            Req(weaponType: "loitering", confidence: 0.5, detectionType: "suspicious_activity"), CancellationToken.None);

        Prop(result, "success").Should().Be(true);
        _detections.Verify(d => d.InsertAsync(It.Is<Detection>(x => x.DetectionType == "suspicious_activity"), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.InsertAsync(It.Is<Notification>(x =>
            x.Type == "suspicious" && x.ImageUrl == "http://img/1.jpg"), It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.InsertAsync(It.Is<Alert>(x =>
            x.Type == "medium" && x.Status == "new" && x.ImageUrl == "http://img/1.jpg"), It.IsAny<CancellationToken>()), Times.Once);
        _broadcaster.Verify(b => b.NotificationCreatedAsync(It.IsAny<object>(), null), Times.Once);
        _broadcaster.Verify(b => b.DetectionOverlayAsync(It.IsAny<object>(), null), Times.Once);
        _broadcaster.Verify(b => b.WeaponDetectedAsync(It.IsAny<object>(), It.IsAny<string?>()), Times.Never);
        _broadcaster.Verify(b => b.AlertCreatedAsync(It.IsAny<object>(), null), Times.Once);
    }

    [Fact]
    public async Task Receive_HitListMatch_PersistsHighPriorityAlertAndScreenshotNotification()
    {
        _detections
            .Setup(d => d.FindRecentAsync("Jane Doe", "Lobby", It.IsAny<DateTime>(), null, ValidUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Detection?)null);
        _detections
            .Setup(d => d.InsertAsync(It.IsAny<Detection>(), It.IsAny<CancellationToken>()))
            .Callback<Detection, CancellationToken>((d, _) => d.Id = "det-hit")
            .Returns(Task.CompletedTask);

        var result = await Sut().Receive(
            Req(weaponType: "Jane Doe", confidence: 0.72, detectionType: "hit_list", userId: ValidUserId),
            CancellationToken.None);

        Prop(result, "success").Should().Be(true);
        _detections.Verify(d => d.InsertAsync(It.Is<Detection>(x =>
            x.DetectionType == "hit_list" && x.UserId == ValidUserId), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.InsertAsync(It.Is<Notification>(x =>
            x.Type == "hit_list" && x.ImageUrl == "http://img/1.jpg"), It.IsAny<CancellationToken>()), Times.Once);
        _alerts.Verify(a => a.InsertAsync(It.Is<Alert>(x =>
            x.Type == "high" && x.DetectionId == "det-hit" && x.ImageUrl == "http://img/1.jpg"), It.IsAny<CancellationToken>()), Times.Once);
        _broadcaster.Verify(b => b.NotificationCreatedAsync(It.IsAny<object>(), ValidUserId), Times.Once);
        _broadcaster.Verify(b => b.AlertCreatedAsync(It.IsAny<object>(), ValidUserId), Times.Once);
        _broadcaster.Verify(b => b.DetectionOverlayAsync(It.IsAny<object>(), ValidUserId), Times.Once);
        _broadcaster.Verify(b => b.WeaponDetectedAsync(It.IsAny<object>(), ValidUserId), Times.Once);
    }

    // ── Email fanout opt-in ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Receive_Weapon_LooksUpUserOnceForEmailFanout(bool emailEnabled)
    {
        var user = new User { Id = ValidUserId, Email = "operator@example.com" };
        user.Settings.Notifications.Email = emailEnabled;
        _users.Setup(u => u.GetByIdAsync(ValidUserId, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _detections
            .Setup(d => d.FindRecentAsync("pistol", "Lobby", It.IsAny<DateTime>(), null, ValidUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Detection?)null);

        var result = await Sut().Receive(Req(userId: ValidUserId), CancellationToken.None);

        Prop(result, "success").Should().Be(true);
        _users.Verify(u => u.GetByIdAsync(ValidUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Receive_Weapon_AnonymousDetection_SkipsUserLookup()
    {
        _detections
            .Setup(d => d.FindRecentAsync("pistol", "Lobby", It.IsAny<DateTime>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Detection?)null);

        await Sut().Receive(Req(), CancellationToken.None);

        _users.Verify(u => u.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
