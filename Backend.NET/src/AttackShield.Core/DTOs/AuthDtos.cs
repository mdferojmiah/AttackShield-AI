namespace AttackShield.Core.DTOs;

// ── Requests ────────────────────────────────────────────────────────────────

/// <summary>
/// User signup. Either <see cref="RtspUrl"/> OR the camera connection fields
/// (Ip/Username/Password) must be present — the API builds the RTSP URL when
/// only connection details are supplied.
/// </summary>
public record RegisterUserRequest(
    string? Name,
    string? Email,
    string? Phone,
    string? Password,
    string? CctvName,
    string? RtspUrl,
    string? Location,
    string? CameraIp,
    string? CameraUsername,
    string? CameraPassword,
    string? CameraPort,
    string? CameraBrand,
    string? CameraPath);

public record RegisterAuthorityRequest(
    string? Name,
    string? Email,
    string? OfficerId,
    string? StationName,
    string? Password);

public record LoginRequest(string? Email, string? Password);

public record ForgotPasswordRequest(string? Email);

public record ResetPasswordRequest(string? Password);

public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public record UpdateProfileRequest(
    string? Name,
    string? Phone,
    string? CctvName,
    string? RtspUrl,
    string? Location);

// ── Responses ───────────────────────────────────────────────────────────────

/// <summary>Camera summary embedded in a user login response.</summary>
public record CameraInfoDto(
    string? Camera_Name,
    string? Stream_Url,
    string? Location,
    string? Rtsp_Url);
