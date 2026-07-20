namespace AttackShield.Core.Interfaces;

/// <summary>
/// Builds an RTSP URL from raw camera connection details, applying brand-specific
/// stream paths and ports (Hikvision, Dahua, Meari, generic). Ported from the
/// generateRtspUrlFromCamera helper in the original authController.
/// </summary>
public interface IRtspUrlBuilder
{
    /// <summary>Returns the RTSP URL, or null when <paramref name="ip"/> is missing.</summary>
    string? Build(
        string? ip,
        string? username,
        string? password,
        string? port = null,
        string? brand = null,
        string? path = null);
}
