namespace AttackShield.Core.DTOs;

/// <summary>
/// Payload posted by the AI service to POST /api/detections/receive.
/// Accepts both cameraName and camera_name (the AI service uses snake_case).
/// </summary>
public record ReceiveDetectionRequest(
    string? WeaponType,
    string? Location,
    double? Confidence,
    string? ImageUrl,
    string? UserId,
    string? CameraName,
    string? Camera_Name,
    string? DetectionType,
    string? CameraId,
    object? Bbox);

/// <summary>Overlay event broadcast to clients over SignalR (detection-overlay).</summary>
public record DetectionOverlayEvent(
    string? CameraId,
    string Type,
    string? Label,
    double Confidence,
    object? Bbox,
    string? Sound,
    string Timestamp);
