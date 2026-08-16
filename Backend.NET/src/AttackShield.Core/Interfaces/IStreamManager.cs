namespace AttackShield.Core.Interfaces;

/// <summary>
/// Manages one FFmpeg process per camera, converting RTSP to HLS segments
/// (served as static files) and emitting MJPEG frames to live HTTP viewers.
/// Implementations own the process lifecycle and must clean up on shutdown.
/// </summary>
public interface IStreamManager
{
    /// <summary>Starts (or no-ops if already running) an FFmpeg stream for a camera.</summary>
    Task<StreamResult> StartAsync(string cameraId, string rtspUrl, CancellationToken ct = default);

    /// <summary>Stops the FFmpeg process for a camera and drops its viewers.</summary>
    Task<StreamResult> StopAsync(string cameraId, CancellationToken ct = default);

    bool IsRunning(string cameraId);

    IReadOnlyCollection<string> ActiveCameraIds { get; }

    /// <summary>
    /// Registers an HTTP response writer as an MJPEG viewer for a camera and blocks
    /// until the client disconnects or the token is cancelled. Returns false when
    /// no active stream exists for the camera.
    /// </summary>
    Task<bool> AttachMjpegViewerAsync(string cameraId, Stream output, CancellationToken ct);

    /// <summary>Stops every stream. Called on application shutdown.</summary>
    Task StopAllAsync();
}

public sealed record StreamResult(bool Success, string? Error, string? StreamPath = null)
{
    public static StreamResult Ok(string? streamPath = null) => new(true, null, streamPath);
    public static StreamResult Fail(string error) => new(false, error);
}
