using System.Globalization;
using System.Text.RegularExpressions;
using AttackShield.Api.Hubs;
using AttackShield.Core.DTOs;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
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
    private readonly IDetectionBroadcaster _broadcaster;
    private readonly ILogger<DetectionsController> _logger;

    // Per-type minimum confidence; must be <= the AI service's own thresholds.
    private static readonly Dictionary<string, double> Thresholds = new()
    {
        ["weapon"] = 0.20,
        ["suspicious_activity"] = 0.15,
        ["face"] = 0.35,
    };

    // Per-type de-dup window in seconds.
    private static readonly Dictionary<string, int> DedupSeconds = new()
    {
        ["weapon"] = 10,
        ["suspicious_activity"] = 30,
    };

    private static readonly Regex ObjectIdPattern = new("^[a-fA-F0-9]{24}$", RegexOptions.Compiled);

    public DetectionsController(
        IDetectionRepository detections,
        INotificationRepository notifications,
        IAlertRepository alerts,
        IDetectionBroadcaster broadcaster,
        ILogger<DetectionsController> logger)
    {
        _detections = detections;
        _notifications = notifications;
        _alerts = alerts;
        _broadcaster = broadcaster;
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
                await _broadcaster.DetectionOverlayAsync(BuildOverlay(req, detectionType, weaponType, confidence, null));

                var recentFace = await _detections.FindRecentAsync(
                    weaponType, location, DateTime.UtcNow.AddSeconds(-30), "face", ct);
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
                weaponType, location, DateTime.UtcNow.AddSeconds(-dedupSec), ct: ct);
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
                    Icon = "eye",
                    UserId = validUserId,
                };
                await _notifications.InsertAsync(notif, ct);

                await _broadcaster.NotificationCreatedAsync(new
                {
                    type = "suspicious",
                    title = notif.Title,
                    description = notif.Description,
                    location,
                    timestamp = notif.CreatedAt,
                });
                await _broadcaster.DetectionOverlayAsync(BuildOverlay(req, detectionType, weaponType, confidence, "suspicious"));

                return Ok(new { success = true, detection = detection.Id, notification = notif.Id });
            }

            // ── Weapon ──
            var weaponNotif = new Notification
            {
                Type = "weapon",
                Title = $"Weapon Detected: {weaponType}",
                Description = $"A {weaponType} was detected at {location}{cameraSuffix} with {confidencePct}% confidence.",
                Location = location,
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
                weaponType, location, confidence, cameraName = camName, timestamp = detection.CreatedAt,
            });
            await _broadcaster.NotificationCreatedAsync(new
            {
                type = "weapon",
                title = weaponNotif.Title,
                description = weaponNotif.Description,
                location,
                timestamp = weaponNotif.CreatedAt,
            });
            await _broadcaster.AlertCreatedAsync(new
            {
                id = alert.Id,
                type = alert.Type,
                title = alert.Title,
                message = alert.Message,
                location,
                cameraName = camName,
                createdAt = alert.CreatedAt,
            });
            await _broadcaster.DetectionOverlayAsync(BuildOverlay(req, detectionType, weaponType, confidence, "weapon"));

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
}
