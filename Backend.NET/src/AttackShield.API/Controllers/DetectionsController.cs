using System.Globalization;
using System.Text.RegularExpressions;
using AttackShield.Api.Hubs;
using AttackShield.Core.DTOs;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using AttackShield.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

/// <summary>
/// Receives detection events from the Python AI service and turns them into
/// persisted detections, notifications, alerts and real-time broadcasts.
/// Direct port of the Node routes/detections.js — thresholds, de-dup windows and
/// the face fast-path are preserved exactly.
/// </summary>
[Route("api/detections")]
public sealed class DetectionsController : ApiControllerBase
{
    private readonly IDetectionRepository _detections;
    private readonly INotificationRepository _notifications;
    private readonly IAlertRepository _alerts;
    private readonly IUserRepository _users;
    private readonly IDetectionBroadcaster _broadcaster;
    private readonly NotificationFanout _fanout;
    private readonly ILogger<DetectionsController> _logger;

    // Per-type minimum confidence; must be <= the AI service's own thresholds.
    private static readonly Dictionary<string, double> Thresholds = new()
    {
        ["weapon"] = 0.60,
        ["suspicious_activity"] = 0.15,
        ["face"] = 0.35,
        // SFace cosine scores for the same person sit near 0.36, which is the
        // AI service's HIT_LIST_MATCH_THRESHOLD. A higher gate here silently
        // dropped genuine hit-list matches the AI service had already accepted.
        ["hit_list"] = 0.36,
    };

    // Per-type de-dup window in seconds.
    private static readonly Dictionary<string, int> DedupSeconds = new()
    {
        ["weapon"] = 10,
        ["suspicious_activity"] = 30,
        ["hit_list"] = 60,
    };

    private static readonly Regex ObjectIdPattern = new("^[a-fA-F0-9]{24}$", RegexOptions.Compiled);

    public DetectionsController(
        IDetectionRepository detections,
        INotificationRepository notifications,
        IAlertRepository alerts,
        IUserRepository users,
        IDetectionBroadcaster broadcaster,
        NotificationFanout fanout,
        ILogger<DetectionsController> logger)
    {
        _detections = detections;
        _notifications = notifications;
        _alerts = alerts;
        _users = users;
        _broadcaster = broadcaster;
        _fanout = fanout;
        _logger = logger;
    }

    [HttpPost("receive")]
    [AllowAnonymous]
    public async Task<IActionResult> Receive([FromBody] ReceiveDetectionRequest req, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.WeaponType) || string.IsNullOrWhiteSpace(req.Location) || req.Confidence is null)
                return Fail("Missing required fields");

            var detectionType = string.IsNullOrWhiteSpace(req.DetectionType) ? "weapon" : req.DetectionType;
            var confidence = req.Confidence.Value;
            var weaponType = req.WeaponType;
            var location = req.Location;
            var camName = req.CameraName ?? req.Camera_Name;

            // Threshold gate.
            var threshold = Thresholds.TryGetValue(detectionType, out var t) ? t : 0.20;
            if (confidence < threshold)
                return Ok(new { success = true, message = "Detection below threshold" });

            var validUserId = !string.IsNullOrEmpty(req.UserId) && ObjectIdPattern.IsMatch(req.UserId!) ? req.UserId : null;

            // ── Faces: emit overlay immediately, save at most every 30s, skip dedup gate ──
            if (detectionType == "face")
            {
                await _broadcaster.DetectionOverlayAsync(BuildOverlay(req, detectionType, weaponType, confidence, null), validUserId);

                var recentFace = await _detections.FindRecentAsync(
                    weaponType, location, DateTime.UtcNow.AddSeconds(-30), "face", validUserId, ct);
                if (recentFace is null)
                {
                    await _detections.InsertAsync(new Detection
                    {
                        WeaponType = weaponType,
                        Location = location,
                        Confidence = confidence,
                        ImageUrl = req.ImageUrl,
                        CameraName = camName,
                        DetectionType = "face",
                        UserId = validUserId,
                    }, ct);
                }
                return Ok(new { success = true });
            }

            // ── De-dup gate for weapon / suspicious_activity ──
            var dedupSec = DedupSeconds.TryGetValue(detectionType, out var d) ? d : 10;
            var existing = await _detections.FindRecentAsync(
                weaponType, location, DateTime.UtcNow.AddSeconds(-dedupSec), userId: validUserId, ct: ct);
            if (existing is not null)
                return Ok(new { success = true, message = "Duplicate ignored" });

            var detection = new Detection
            {
                WeaponType = weaponType,
                Location = location,
                Confidence = confidence,
                ImageUrl = req.ImageUrl,
                CameraName = camName,
                DetectionType = detectionType,
                UserId = validUserId,
            };
            await _detections.InsertAsync(detection, ct);

            var confidencePct = (confidence * 100).ToString("F1", CultureInfo.InvariantCulture);
            var cameraSuffix = string.IsNullOrEmpty(camName) ? "" : $" (Camera: {camName})";

            // ── Suspicious activity ──
            if (detectionType == "suspicious_activity")
            {
                var notif = new Notification
                {
                    Type = "suspicious",
                    Title = $"Suspicious Activity: {weaponType}",
                    Description = $"Suspicious activity \"{weaponType}\" detected at {location}{cameraSuffix} with {confidencePct}% confidence.",
                    Location = location,
                    ImageUrl = req.ImageUrl,
                    Icon = "eye",
                    UserId = validUserId,
                };
                await _notifications.InsertAsync(notif, ct);

                var suspiciousAlert = new Alert
                {
                    Type = "medium",
                    Title = $"Suspicious Activity: {weaponType}",
                    Message = $"Detected at {location}{cameraSuffix}",
                    Location = location,
                    ImageUrl = req.ImageUrl,
                    DetectionId = detection.Id,
                    CameraName = camName,
                    Status = "new",
                    UserId = validUserId,
                };
                await _alerts.InsertAsync(suspiciousAlert, ct);

                await _broadcaster.NotificationCreatedAsync(new
                {
                    id = notif.Id,
                    _id = notif.Id,
                    type = "suspicious",
                    title = notif.Title,
                    description = notif.Description,
                    location,
                    imageUrl = notif.ImageUrl,
                    createdAt = notif.CreatedAt,
                }, validUserId);
                await _broadcaster.AlertCreatedAsync(new
                {
                    id = suspiciousAlert.Id,
                    type = suspiciousAlert.Type,
                    title = suspiciousAlert.Title,
                    message = suspiciousAlert.Message,
                    location,
                    cameraName = camName,
                    imageUrl = suspiciousAlert.ImageUrl,
                    createdAt = suspiciousAlert.CreatedAt,
                }, validUserId);
                await _broadcaster.DetectionOverlayAsync(BuildOverlay(req, detectionType, weaponType, confidence, "suspicious"), validUserId);
                await PublishFanoutAsync(new
                {
                    type = "suspicious_activity",
                    title = notif.Title,
                    message = notif.Description,
                    location,
                    confidence,
                    cameraName = camName,
                    imageUrl = req.ImageUrl,
                    createdAt = detection.CreatedAt,
                }, validUserId, ct);

                return Ok(new { success = true, detection = detection.Id, notification = notif.Id, alert = suspiciousAlert.Id });
            }

            if (detectionType == "hit_list")
            {
                var hitNotification = new Notification
                {
                    Type = "hit_list",
                    Title = $"Hit List Match: {weaponType}",
                    Description = $"{weaponType} was recognized at {location}{cameraSuffix} with {confidencePct}% similarity.",
                    Location = location,
                    ImageUrl = req.ImageUrl,
                    Icon = "user-alert",
                    UserId = validUserId,
                };
                await _notifications.InsertAsync(hitNotification, ct);

                var hitAlert = new Alert
                {
                    Type = "high",
                    Title = $"Hit List Match: {weaponType}",
                    Message = $"Recognized at {location}{cameraSuffix}",
                    Location = location,
                    ImageUrl = req.ImageUrl,
                    DetectionId = detection.Id,
                    CameraName = camName,
                    Status = "new",
                    UserId = validUserId,
                };
                await _alerts.InsertAsync(hitAlert, ct);
                await _broadcaster.NotificationCreatedAsync(new
                {
                    id = hitNotification.Id,
                    _id = hitNotification.Id,
                    type = "hit_list",
                    title = hitNotification.Title,
                    description = hitNotification.Description,
                    location,
                    imageUrl = hitNotification.ImageUrl,
                    createdAt = hitNotification.CreatedAt,
                }, validUserId);
                await _broadcaster.AlertCreatedAsync(new
                {
                    id = hitAlert.Id,
                    type = hitAlert.Type,
                    title = hitAlert.Title,
                    message = hitAlert.Message,
                    location,
                    cameraName = camName,
                    imageUrl = hitAlert.ImageUrl,
                    createdAt = hitAlert.CreatedAt,
                }, validUserId);
                await _broadcaster.WeaponDetectedAsync(new
                {
                    type = "hit_list",
                    weaponType,
                    location,
                    confidence,
                    cameraName = camName,
                    timestamp = detection.CreatedAt,
                }, validUserId);
                await _broadcaster.DetectionOverlayAsync(BuildOverlay(req, detectionType, weaponType, confidence, "hit_list"), validUserId);
                await PublishFanoutAsync(new
                {
                    type = "hit_list",
                    title = hitNotification.Title,
                    message = hitNotification.Description,
                    location,
                    confidence,
                    cameraName = camName,
                    imageUrl = req.ImageUrl,
                    createdAt = detection.CreatedAt,
                }, validUserId, ct);
                return Ok(new { success = true, detection = detection.Id, notification = hitNotification.Id, alert = hitAlert.Id });
            }

            // ── Weapon ──
            var weaponNotif = new Notification
            {
                Type = "weapon",
                Title = $"Weapon Detected: {weaponType}",
                Description = $"A {weaponType} was detected at {location}{cameraSuffix} with {confidencePct}% confidence.",
                Location = location,
                ImageUrl = req.ImageUrl,
                Icon = "alert-triangle",
                UserId = validUserId,
            };
            await _notifications.InsertAsync(weaponNotif, ct);

            var alert = new Alert
            {
                Type = "high",
                Title = $"Weapon Detected: {weaponType}",
                Message = $"Detected at {location}{cameraSuffix}",
                Location = location,
                ImageUrl = req.ImageUrl,
                DetectionId = detection.Id,
                CameraName = camName,
                Status = "new",
                UserId = validUserId,
            };
            await _alerts.InsertAsync(alert, ct);

            await _broadcaster.WeaponDetectedAsync(new
            {
                type = "weapon", weaponType, location, confidence, cameraName = camName, timestamp = detection.CreatedAt,
            }, validUserId);
            await _broadcaster.NotificationCreatedAsync(new
            {
                id = weaponNotif.Id,
                _id = weaponNotif.Id,
                type = "weapon",
                title = weaponNotif.Title,
                description = weaponNotif.Description,
                location,
                imageUrl = weaponNotif.ImageUrl,
                createdAt = weaponNotif.CreatedAt,
            }, validUserId);
            await _broadcaster.AlertCreatedAsync(new
            {
                id = alert.Id,
                type = alert.Type,
                title = alert.Title,
                message = alert.Message,
                location,
                cameraName = camName,
                imageUrl = alert.ImageUrl,
                createdAt = alert.CreatedAt,
            }, validUserId);
            await PublishFanoutAsync(new
            {
                type = "weapon",
                title = weaponNotif.Title,
                message = weaponNotif.Description,
                location,
                confidence,
                cameraName = camName,
                imageUrl = req.ImageUrl,
                createdAt = detection.CreatedAt,
            }, validUserId, ct);
            await _broadcaster.DetectionOverlayAsync(BuildOverlay(req, detectionType, weaponType, confidence, "weapon"), validUserId);

            return Ok(new { success = true, detection = detection.Id, notification = weaponNotif.Id, alert = alert.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing detection");
            return Fail("Internal server error", 500);
        }
    }

    private static object BuildOverlay(ReceiveDetectionRequest req, string type, string? label, double confidence, string? sound)
        => new
        {
            cameraId = req.CameraId,
            type,
            label,
            confidence,
            bbox = req.Bbox,
            sound,
            timestamp = DateTime.UtcNow.ToString("o"),
        };

    private async Task PublishFanoutAsync(object payload, string? userId, CancellationToken ct)
    {
        var email = userId is null ? null : (await _users.GetByIdAsync(userId, ct))?.Email;
        await _fanout.PublishAsync(payload, email, ct);
    }
}
