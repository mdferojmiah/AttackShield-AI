namespace AttackShield.Core.DTOs;

/// <summary>Camera entry returned by GET /api/cameras (primary + extras).</summary>
public record CameraDto(
    string Id,
    string? Name,
    string? RtspUrl,
    string? Location,
    string? Brand);

/// <summary>Body for POST /api/cameras. Either RtspUrl or camera details are required.</summary>
public record AddCameraRequest(
    string? Name,
    string? Location,
    string? RtspUrl,
    string? CameraIp,
    string? CameraUsername,
    string? CameraPassword,
    string? CameraPort,
    string? CameraBrand,
    string? CameraPath);
