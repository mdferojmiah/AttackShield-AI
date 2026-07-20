using System.Text.Json;
using AttackShield.Core.DTOs;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

/// <summary>
/// User application preferences. Ported from the Node settingsController.
/// Both the nested groups (notifications/detection/app) and the flat convenience
/// keys are accepted on update and merged into the user's settings sub-document;
/// the response is always the flattened shape produced by <see cref="ToFlat"/>.
/// Authenticated.
/// </summary>
[Route("api/settings")]
[Authorize]
public sealed class SettingsController : ApiControllerBase
{
    private readonly IUserRepository _users;

    public SettingsController(IUserRepository users)
    {
        _users = users;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var id = CurrentUserId;
        if (id is null) return Fail("Unauthorized", 401);

        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return Fail("User not found", 404);

        return Ok(new { success = true, data = ToFlat(user.Settings) });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest req, CancellationToken ct)
    {
        var id = CurrentUserId;
        if (id is null) return Fail("Unauthorized", 401);

        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return Fail("User not found", 404);

        var s = user.Settings;

        // ── Shallow-merge nested groups when provided directly ──
        MergeNotifications(s.Notifications, req.Notifications);
        MergeDetection(s.Detection, req.Detection);
        MergeApp(s.App, req.App);

        // ── Map flat convenience keys → nested structure ──
        if (req.NotificationsEnabled is not null) s.Notifications.Push = req.NotificationsEnabled.Value;
        if (req.SoundEnabled is not null) s.Notifications.Sound = req.SoundEnabled.Value;
        if (req.VibrationEnabled is not null) s.Notifications.Vibration = req.VibrationEnabled.Value;
        if (req.DetectionSensitivity is not null) s.Detection.Sensitivity = req.DetectionSensitivity;
        if (req.AlertThreshold is not null) s.Detection.AlertThreshold = req.AlertThreshold.Value;
        if (req.DarkMode is not null) s.App.Theme = req.DarkMode.Value ? "dark" : "light";
        if (req.AutoStartMonitoring is not null) s.Detection.AutoStartMonitoring = req.AutoStartMonitoring.Value;

        user.UpdatedAt = DateTime.UtcNow;
        await _users.UpdateAsync(id, user, ct);

        return Ok(new { success = true, data = ToFlat(s) });
    }

    // ── Nested-group merges. Values arrive as JsonElement (deserialized to object). ──
    private static void MergeNotifications(NotificationSettings target, Dictionary<string, object>? group)
    {
        if (group is null) return;
        if (TryBool(group, "push", out var push)) target.Push = push;
        if (TryBool(group, "sound", out var sound)) target.Sound = sound;
        if (TryBool(group, "vibration", out var vibration)) target.Vibration = vibration;
    }

    private static void MergeDetection(DetectionSettings target, Dictionary<string, object>? group)
    {
        if (group is null) return;
        if (TryString(group, "sensitivity", out var sensitivity)) target.Sensitivity = sensitivity;
        if (TryInt(group, "alertThreshold", out var threshold)) target.AlertThreshold = threshold;
        if (TryBool(group, "autoStartMonitoring", out var autoStart)) target.AutoStartMonitoring = autoStart;
    }

    private static void MergeApp(AppSettings target, Dictionary<string, object>? group)
    {
        if (group is null) return;
        if (TryString(group, "theme", out var theme)) target.Theme = theme;
    }

    /// <summary>Convert nested settings to the flat convenience keys the frontend uses.</summary>
    private static object ToFlat(UserSettings s) => new
    {
        notifications = s.Notifications,
        detection = s.Detection,
        app = s.App,
        notificationsEnabled = s.Notifications.Push,
        soundEnabled = s.Notifications.Sound,
        vibrationEnabled = s.Notifications.Vibration,
        detectionSensitivity = s.Detection.Sensitivity,
        alertThreshold = s.Detection.AlertThreshold,
        darkMode = s.App.Theme == "dark",
        autoStartMonitoring = s.Detection.AutoStartMonitoring,
    };

    // ── JsonElement coercion helpers ──
    private static bool TryBool(Dictionary<string, object> group, string key, out bool value)
    {
        value = default;
        if (!group.TryGetValue(key, out var raw) || raw is not JsonElement el) return false;
        if (el.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = el.GetBoolean();
            return true;
        }
        return false;
    }

    private static bool TryInt(Dictionary<string, object> group, string key, out int value)
    {
        value = default;
        return group.TryGetValue(key, out var raw) && raw is JsonElement { ValueKind: JsonValueKind.Number } el
            && el.TryGetInt32(out value);
    }

    private static bool TryString(Dictionary<string, object> group, string key, out string value)
    {
        value = string.Empty;
        if (!group.TryGetValue(key, out var raw) || raw is not JsonElement { ValueKind: JsonValueKind.String } el)
            return false;
        value = el.GetString() ?? string.Empty;
        return true;
    }
}
