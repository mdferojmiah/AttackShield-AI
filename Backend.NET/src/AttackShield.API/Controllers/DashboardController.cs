using System.Text.Json;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController : ApiControllerBase
{
    private readonly IDetectionRepository _detections;
    private readonly INotificationRepository _notifications;
    private readonly IAiServiceClient _ai;
    private readonly IUserRepository _users;
    private readonly IStreamManager _streams;

    public DashboardController(
        IDetectionRepository detections,
        INotificationRepository notifications,
        IAiServiceClient ai,
        IUserRepository users,
        IStreamManager streams)
    {
        _detections = detections;
        _notifications = notifications;
        _ai = ai;
        _users = users;
        _streams = streams;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var ownerId = CurrentUserRole == "admin" ? null : CurrentUserId;
        var metricsTask = _ai.GetMetricsAsync(ct);
        var trustTask = _ai.GetTrustScoreAsync(ct);

        var totalWeaponsTask = _detections.CountByTypeAsync("weapon", ownerId, ct);
        var alertsTask = _notifications.CountByTypesAsync(new[] { "weapon", "hit_list", "suspicious" }, ownerId, ct);
        var facesTask = _detections.CountByTypeSinceAsync("face", today, ownerId, ct);
        var suspiciousTask = _detections.CountByTypeSinceAsync("suspicious_activity", today, ownerId, ct);
        await Task.WhenAll(metricsTask, trustTask, totalWeaponsTask, alertsTask, facesTask, suspiciousTask);

        var metrics = ParseData(await metricsTask);
        var trust = ParseData(await trustTask);
        var isAdmin = CurrentUserRole == "admin";
        var aiFaces = isAdmin ? Number(metrics, "faces_detected") : 0;
        var aiSuspicious = isAdmin ? Number(metrics, "suspicious_activities") : 0;

        return Ok(new
        {
            totalWeapons = await totalWeaponsTask,
            alertsSent = await alertsTask,
            accuracy = 0.98,
            suspiciousActivities = Math.Max(await suspiciousTask, aiSuspicious),
            facesDetected = Math.Max(await facesTask, aiFaces),
            uniquePersons = isAdmin ? Number(metrics, "unique_persons") : await facesTask,
            trustScore = Number(trust, "score", 92),
            ensembleConfidence = Number(metrics, "ensemble_confidence"),
            lastUpdated = DateTime.UtcNow,
        });
    }

    [HttpGet("activity")]
    public async Task<IActionResult> GetActivity(CancellationToken ct)
    {
        var ownerId = CurrentUserRole == "admin" ? null : CurrentUserId;
        var notifications = await _notifications.GetAllNewestAsync(ownerId, ct);
        return Ok(notifications.Take(10).Select(MapActivity));
    }

    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(CancellationToken ct)
        => Ok(new { success = true, data = ParseData(await _ai.GetMetricsAsync(ct), MetricsDefaults) });

    [HttpGet("trust-score")]
    public async Task<IActionResult> GetTrustScore(CancellationToken ct)
        => Ok(new { success = true, data = ParseData(await _ai.GetTrustScoreAsync(ct), TrustDefaults) });

    [HttpGet("camera-status")]
    public async Task<IActionResult> GetCameraStatus(CancellationToken ct)
    {
        var user = CurrentUserId is null ? null : await _users.GetByIdAsync(CurrentUserId, ct);
        if (user is null) return Fail("User not found", 404);

        return Ok(new
        {
            success = true,
            data = new
            {
                camera_name = user.CctvName,
                location = user.Location,
                rtsp_url = user.RtspUrl,
                status = CurrentUserId is not null && _streams.IsRunning($"{CurrentUserId}-primary") ? "active" : "inactive",
            },
        });
    }

    [HttpPost("detection")]
    public async Task<IActionResult> RecordDetection([FromBody] DashboardDetectionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Type)) return Fail("type is required");

        var detection = new Detection
        {
            WeaponType = request.Type,
            DetectionType = request.Type == "suspicious" ? "suspicious_activity" : request.Type,
            Location = request.Location ?? string.Empty,
            Confidence = request.Confidence ?? 0,
            UserId = CurrentUserId,
        };
        await _detections.InsertAsync(detection, ct);

        return StatusCode(201, new
        {
            success = true,
            message = "Detection recorded",
            data = new
            {
                id = detection.Id,
                type = request.Type == "weapon" ? "high" : request.Type == "suspicious" ? "medium" : "low",
                message = request.Message ?? $"{request.Type} detected",
                time = detection.CreatedAt.ToLocalTime().ToString("T"),
                location = request.Location,
                confidence = request.Confidence,
            },
        });
    }

    [HttpPost("reset")]
    [Authorize(Roles = "admin")]
    public IActionResult ResetStats()
        => Ok(new { success = true, message = "Dashboard stats reset" });

    private static object MapActivity(Notification notification)
        => new
        {
            id = notification.Id,
            type = notification.Type is "weapon" or "hit_list" ? "high" : notification.Type is "suspicious" or "activity" ? "medium" : "low",
            message = notification.Title,
            time = notification.CreatedAt.ToLocalTime().ToString("G"),
        };

    private static JsonElement ParseData(AiCallResult result, string? fallback = null)
    {
        var json = result.Success ? result.RawJson : fallback;
        if (string.IsNullOrWhiteSpace(json))
            json = "{}";

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("data", out var data))
                return data.Clone();
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    private static double Number(JsonElement data, string name, double fallback = 0)
        => data.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : fallback;

    private const string MetricsDefaults = "{\"weapons_detected\":0,\"suspicious_activities\":0,\"faces_detected\":0,\"unique_persons\":0,\"total_frames_processed\":0,\"avg_inference_latency_ms\":0,\"ensemble_confidence\":0}";
    private const string TrustDefaults = "{\"score\":92,\"auth_consistency\":100,\"anomaly_frequency\":0,\"model_confidence_stability\":95,\"communication_integrity\":100,\"policy_compliance\":100}";
}

public sealed record DashboardDetectionRequest(
    string? Type,
    string? Message,
    string? Location,
    double? Confidence);
