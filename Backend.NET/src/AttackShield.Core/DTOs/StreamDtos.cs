namespace AttackShield.Core.DTOs;

/// <summary>
/// Body for POST /api/stream/start and /stop. cameraId is optional — when omitted
/// the primary camera ("primary") is assumed. rtspUrl may be supplied to override
/// the stored camera URL.
/// </summary>
public record StreamStartRequest(string? CameraId, string? RtspUrl);

public record StreamStopRequest(string? CameraId);

/// <summary>Body for POST /api/stream/webcam (local webcam index for testing).</summary>
public record WebcamRequest(string? CameraId);
