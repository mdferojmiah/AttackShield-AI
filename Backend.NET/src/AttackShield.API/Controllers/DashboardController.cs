using System.Text.Json;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

/// <summary>
/// Dashboard data endpoints. Ported from the Node dashboardController.
/// Stats are computed from Mongo counts (the durable floor) and supplemented with
/// the AI service's in-session metrics; metrics/trust-score are passed through from
/// <see cref="IAiServiceClient"/> with the same fallback defaults as the original.
/// stats/activity/metrics/trust-score use optional auth (public read).
/// </summary>
[Route("api/dashboard")]
public sealed class DashboardController : ApiControllerBase
{
    private readonly IDetectionRepository _detections;
    private readonly INotificationRepository _notifications;
    private readonly IUserRepository _users;
    private readonly IAiServiceClient _ai;

    public DashboardController(
        IDetectionRepository detections,
        INotificationRepository notifications,
        IUserRepository users,
        IAiServiceClient ai)
    {
        _detections = detections;
        _notifications = notifications;
        _users = users;
        _ai = ai;
    }

    [HttpGet("stats")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        // ── DB counts (persist across AI service restarts) ──
        var today = DateTime.UtcNow.Date;

        var totalWeaponsTask = _detections.CountByTypeAsync("weapon", ct);
        var alertsSentTask = _notifications.CountByTypesAsync(new[] { "weapon", "suspicious" }, ct);
        var facesDetectedDbTask = _detections.CountByTypeSinceAsync("face", today, ct);
        var suspiciousDbTask = _detections.CountByTypeSinceAsync("suspicious_activity", today, ct);

        await Task.WhenAll(totalWeaponsTask, alertsSentTask, facesDetectedDbTask, suspiciousDbTask);

        var totalWeapons = totalWeaponsTask.Result;
        var alertsSent = alertsSentTask.Result;
        var facesDetectedDb = facesDetectedDbTask.Result;
        var suspiciousDb = suspiciousDbTask.Result;

        const double accuracy = 0.98;

        // ── AI service metrics (in-session, used as supplement) ──
        long aiSuspicious = 0, aiFaces = 0, aiUniquePersons = 0;
        double trustScore = 92.0, ensembleConfidence = 0;

        var metrics = await _ai.GetMetricsAsync(ct);
        if (metrics.Success && TryData(metrics.RawJson, out var m))
        {
            aiSuspicious = GetLong(m, "suspicious_activities");
            aiFaces = GetLong(m, "faces_detected");
            aiUniquePersons = GetLong(m, "unique_persons");
            ensembleConfidence = GetDouble(m, "ensemble_confidence");
        }

        var trust = await _ai.GetTrustScoreAsync(ct);
        if (trust.Success && TryData(trust.RawJson, out var t) && t.TryGetProperty("score", out var scoreEl))
            trustScore = scoreEl.GetDouble();

        // DB counts are the floor; AI in-session counts supplement them.
        var facesDetected = Math.Max(facesDetectedDb, aiFaces);
        var suspiciousActivities = Math.Max(suspiciousDb, aiSuspicious);
        // uniquePersons: use the AI tracker value (true unique count) when running.
        var uniquePersons = aiUniquePersons > 0 ? aiUniquePersons : 0;

        return Ok(new
        {
            totalWeapons,
            alertsSent,
            accuracy,
            suspiciousActivities,
            facesDetected,
            uniquePersons,
            trustScore,
            ensembleConfidence,
            lastUpdated = DateTime.UtcNow.ToString("o"),
        });
    }

    [HttpGet("activity")]
    [AllowAnonymous]
    public async Task<IActionResult> GetActivity(CancellationToken ct)
    {
        var recent = await _notifications.GetAllNewestAsync(ct);

        var activities = recent.Take(10).Select(n => new
        {
            id = n.Id,
            type = n.Type == "weapon"
                ? "high"
                : (n.Type is "suspicious" or "activity")
                    ? "medium"
                    : "low", // face, camera, system → low
            message = n.Title,
            time = n.CreatedAt.ToLocalTime().ToString("G"),
        });

        return Ok(activities);
    }

    [HttpGet("camera-status")]
    [Authorize]
    public async Task<IActionResult> GetCameraStatus(CancellationToken ct)
    {
        var id = CurrentUserId;
        if (id is null) return Fail("Unauthorized", 401);

        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return Fail("User not found", 404);

        return Ok(new
        {
            success = true,
            data = new
            {
                camera_name = user.CctvName,
                location = user.Location,
                rtsp_url = user.RtspUrl,
                status = "active", // In production, check actual stream status.
            },
        });
    }

    [HttpPost("detection")]
    [Authorize]
    public IActionResult RecordDetection([FromBody] RecordDetectionRequest req)
    {
        // Mirrors the original: acknowledges the event and echoes a shaped activity.
        // Persistence proper happens via POST /api/detections/receive.
        var activity = new
        {
            id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            type = req.Type == "weapon" ? "high" : req.Type == "suspicious" ? "medium" : "low",
            message = string.IsNullOrWhiteSpace(req.Message) ? $"{req.Type} detected" : req.Message,
            time = DateTime.UtcNow.ToLocalTime().ToString("T"),
            location = req.Location,
            confidence = req.Confidence,
        };

        return StatusCode(201, new { success = true, message = "Detection recorded", data = activity });
    }

    [HttpPost("reset")]
    [Authorize(Roles = "admin")]
    public IActionResult ResetStats()
        // The original only cleared in-memory counters; DB stats are derived, so
        // there is nothing durable to reset. Kept for API parity (admin only).
        => Ok(new { success = true, message = "Dashboard stats reset" });

    [HttpGet("metrics")]
    [AllowAnonymous]
    public async Task<IActionResult> GetMetrics(CancellationToken ct)
    {
        var result = await _ai.GetMetricsAsync(ct);
        if (result.Success && result.RawJson is not null)
            return Content(result.RawJson, "application/json");

        // AI service unavailable → same fallback shape as the Node version.
        return Ok(new
        {
            success = true,
            data = new
            {
                weapons_detected = 0,
                suspicious_activities = 0,
                faces_detected = 0,
                total_frames_processed = 0,
                avg_inference_latency_ms = 0,
                ensemble_confidence = 0,
            },
        });
    }

    [HttpGet("trust-score")]
    [AllowAnonymous]
    public async Task<IActionResult> GetTrustScore(CancellationToken ct)
    {
        var result = await _ai.GetTrustScoreAsync(ct);
        if (result.Success && result.RawJson is not null)
            return Content(result.RawJson, "application/json");

        return Ok(new
        {
            success = true,
            data = new
            {
                score = 92.0,
                auth_consistency = 100,
                anomaly_frequency = 0,
                model_confidence_stability = 95,
                communication_integrity = 100,
                policy_compliance = 100,
            },
        });
    }

    // ── AI JSON helpers ────────────────────────────────────────────────
    // The AI responses wrap payload in { data: {...} }; pull that object out.
    private static bool TryData(string? json, out JsonElement data)
    {
        data = default;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object)
            {
                data = d.Clone();
                return true;
            }
        }
        catch (JsonException)
        {
            // Malformed payload — treat as unavailable.
        }
        return false;
    }

    private static long GetLong(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt64() : 0;

    private static double GetDouble(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetDouble() : 0;
}

/// <summary>Body for POST /api/dashboard/detection (lightweight activity echo).</summary>
public sealed record RecordDetectionRequest(string? Type, string? Message, string? Location, double? Confidence);
