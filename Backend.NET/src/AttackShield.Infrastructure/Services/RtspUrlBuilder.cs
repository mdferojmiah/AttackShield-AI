using AttackShield.Core.Interfaces;

namespace AttackShield.Infrastructure.Services;

/// <summary>
/// Direct port of generateRtspUrlFromCamera from the original authController.
/// Brand-specific defaults: Hikvision, Dahua, Meari (port 8554), generic Dahua-style.
/// </summary>
public sealed class RtspUrlBuilder : IRtspUrlBuilder
{
    public string? Build(
        string? ip,
        string? username,
        string? password,
        string? port = null,
        string? brand = null,
        string? path = null)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return null;

        var normalizedBrand = (brand ?? string.Empty).ToLowerInvariant();
        var streamPath = path;

        if (string.IsNullOrEmpty(streamPath))
        {
            if (normalizedBrand.Contains("hikvision"))
                streamPath = "/Streaming/Channels/101";
            else if (normalizedBrand.Contains("dahua"))
                streamPath = "/cam/realmonitor?channel=1&subtype=1";
            else if (normalizedBrand.Contains("meari"))
                streamPath = "/live";
            else
                streamPath = "/cam/realmonitor?channel=1&subtype=1";
        }

        // Parse the supplied port; default to 554. Meari uses 8554 when unspecified.
        int finalPort = 554;
        bool portSupplied = int.TryParse(port, out var parsedPort) && parsedPort > 0;
        if (portSupplied)
            finalPort = parsedPort;
        else if (normalizedBrand.Contains("meari"))
            finalPort = 8554;

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            var u = Uri.EscapeDataString(username);
            var p = Uri.EscapeDataString(password);
            return $"rtsp://{u}:{p}@{ip}:{finalPort}{streamPath}";
        }

        return $"rtsp://{ip}:{finalPort}{streamPath}";
    }
}
