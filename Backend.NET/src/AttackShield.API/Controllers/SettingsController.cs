using System.Text.Json;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

[Authorize]
public sealed class SettingsController : ApiControllerBase
{
    private readonly IUserRepository _users;

    public SettingsController(IUserRepository users) => _users = users;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await GetUser(ct);
        if (user is null) return Fail("User not found", 404);
        return Ok(new { success = true, data = ToFlat(user.Settings) });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] JsonElement body, CancellationToken ct)
    {
        var user = await GetUser(ct);
        if (user is null) return Fail("User not found", 404);

        var updates = body;
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("settings", out var nested))
            updates = nested;

        if (updates.ValueKind != JsonValueKind.Object)
            return Fail("Settings must be an object");

        ApplyUpdates(user.Settings, updates);
        user.UpdatedAt = DateTime.UtcNow;
        await _users.UpdateAsync(user.Id!, user, ct);
        return Ok(new { success = true, data = ToFlat(user.Settings) });
    }

    private Task<User?> GetUser(CancellationToken ct)
        => CurrentUserId is null ? Task.FromResult<User?>(null) : _users.GetByIdAsync(CurrentUserId, ct);

    private static object ToFlat(UserSettings settings) => new
    {
        notifications = settings.Notifications,
        detection = settings.Detection,
        app = settings.App,
        notificationsEnabled = settings.Notifications.Push,
        soundEnabled = settings.Notifications.Sound,
        vibrationEnabled = settings.Notifications.Vibration,
        detectionSensitivity = settings.Detection.Sensitivity,
        alertThreshold = settings.Detection.AlertThreshold,
        darkMode = settings.App.Theme == "dark",
        autoStartMonitoring = settings.Detection.AutoStartMonitoring,
    };

    private static void ApplyUpdates(UserSettings settings, JsonElement updates)
    {
        MergeGroup(updates, "notifications", value =>
        {
            SetBool(value, "push", v => settings.Notifications.Push = v);
            SetBool(value, "sound", v => settings.Notifications.Sound = v);
            SetBool(value, "vibration", v => settings.Notifications.Vibration = v);
        });
        MergeGroup(updates, "detection", value =>
        {
            SetString(value, "sensitivity", v => settings.Detection.Sensitivity = v);
            SetInt(value, "alertThreshold", v => settings.Detection.AlertThreshold = v);
            SetBool(value, "autoStartMonitoring", v => settings.Detection.AutoStartMonitoring = v);
        });
        MergeGroup(updates, "app", value => SetString(value, "theme", v => settings.App.Theme = v));

        SetBool(updates, "notificationsEnabled", v => settings.Notifications.Push = v);
        SetBool(updates, "soundEnabled", v => settings.Notifications.Sound = v);
        SetBool(updates, "vibrationEnabled", v => settings.Notifications.Vibration = v);
        SetString(updates, "detectionSensitivity", v => settings.Detection.Sensitivity = v);
        SetInt(updates, "alertThreshold", v => settings.Detection.AlertThreshold = v);
        SetBool(updates, "darkMode", v => settings.App.Theme = v ? "dark" : "light");
        SetBool(updates, "autoStartMonitoring", v => settings.Detection.AutoStartMonitoring = v);
    }

    private static void MergeGroup(JsonElement parent, string name, Action<JsonElement> apply)
    {
        if (parent.TryGetProperty(name, out var group) && group.ValueKind == JsonValueKind.Object)
            apply(group);
    }

    private static void SetBool(JsonElement parent, string name, Action<bool> apply)
    {
        if (parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            apply(value.GetBoolean());
    }

    private static void SetString(JsonElement parent, string name, Action<string> apply)
    {
        if (parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            apply(value.GetString()!);
    }

    private static void SetInt(JsonElement parent, string name, Action<int> apply)
    {
        if (parent.TryGetProperty(name, out var value) && value.TryGetInt32(out var number))
            apply(number);
    }
}
