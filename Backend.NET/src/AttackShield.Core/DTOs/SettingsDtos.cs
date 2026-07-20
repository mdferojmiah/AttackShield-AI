namespace AttackShield.Core.DTOs;

/// <summary>
/// Partial settings update. All fields optional; both the nested groups and the
/// flat convenience keys are accepted (mirrors the original settingsController).
/// </summary>
public record UpdateSettingsRequest(
    // nested groups
    Dictionary<string, object>? Notifications,
    Dictionary<string, object>? Detection,
    Dictionary<string, object>? App,
    // flat convenience keys
    bool? NotificationsEnabled,
    bool? SoundEnabled,
    bool? VibrationEnabled,
    string? DetectionSensitivity,
    int? AlertThreshold,
    bool? DarkMode,
    bool? AutoStartMonitoring);
