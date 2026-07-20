using AttackShield.Core.DTOs;
using AttackShield.Core.Interfaces;
using AttackShield.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AttackShield.Api.Controllers;

/// <summary>
/// RTSP→HLS streaming control plus the MJPEG relay and static HLS file serving.
/// Ported from the Node streamController/stream routes. Control routes are
/// authenticated; the MJPEG viewer and HLS files are unauthenticated because an
/// &lt;img&gt;/&lt;video&gt; tag cannot attach an Authorization header (same as the
/// original). The actual FFmpeg lifecycle lives in <see cref="IStreamManager"/>.
/// </summary>
[Route("api/stream")]
public sealed class StreamController : ApiControllerBase
{
    private readonly IStreamManager _streams;
    private readonly IUserRepository _users;
    private readonly string _streamsRoot;

    public StreamController(
        IStreamManager streams,
        IUserRepository users,
        IOptions<StreamOptions> streamOptions,
        IHostEnvironment env)
    {
        _streams = streams;
        _users = users;

        // Resolve the streams root exactly as FfmpegStreamManager does so the
        // static HLS handler reads from the same directory FFmpeg writes to.
        var root = streamOptions.Value.StreamsRoot;
        _streamsRoot = Path.IsPathRooted(root) ? root : Path.Combine(env.ContentRootPath, root);
    }

    [HttpPost("start")]
    [Authorize]
    public async Task<IActionResult> Start([FromBody] StreamStartRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CameraId) || string.IsNullOrWhiteSpace(req.RtspUrl))
            return Fail("cameraId and rtspUrl are required");

        var result = await _streams.StartAsync(req.CameraId, req.RtspUrl, ct);
        if (!result.Success)
            return Fail(result.Error ?? "Failed to start stream", 500);

        return Ok(new
        {
            success = true,
            message = $"Stream started for camera {req.CameraId}",
            hlsUrl = HlsUrl(req.CameraId),
        });
    }

    [HttpPost("stop")]
    [Authorize]
    public async Task<IActionResult> Stop([FromBody] StreamStopRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CameraId))
            return Fail("cameraId is required");

        await _streams.StopAsync(req.CameraId, ct);
        return Ok(new { success = true, message = $"Stream stopped for camera {req.CameraId}" });
    }

    [HttpPost("start-all")]
    [Authorize]
    public async Task<IActionResult> StartAll(CancellationToken ct)
    {
        var id = CurrentUserId;
        if (id is null) return Fail("Unauthorized", 401);

        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return Fail("User not found", 404);

        var started = new List<object>();

        // Primary camera.
        if (!string.IsNullOrWhiteSpace(user.RtspUrl))
        {
            await _streams.StartAsync("primary", user.RtspUrl, ct);
            started.Add(new { cameraId = "primary", hlsUrl = HlsUrl("primary") });
        }

        // Extra cameras.
        for (var i = 0; i < user.Cameras.Count; i++)
        {
            var cam = user.Cameras[i];
            var camId = cam.Id ?? $"extra-{i}";
            if (!string.IsNullOrWhiteSpace(cam.RtspUrl))
            {
                await _streams.StartAsync(camId, cam.RtspUrl, ct);
                started.Add(new { cameraId = camId, hlsUrl = HlsUrl(camId) });
            }
        }

        return Ok(new { success = true, message = $"Started {started.Count} stream(s)", streams = started });
    }

    [HttpPost("stop-all")]
    [Authorize]
    public async Task<IActionResult> StopAll(CancellationToken ct)
    {
        var stopped = _streams.ActiveCameraIds.ToList();
        foreach (var camId in stopped)
            await _streams.StopAsync(camId, ct);

        return Ok(new { success = true, message = $"Stopped {stopped.Count} stream(s)", stopped });
    }

    [HttpPost("webcam")]
    [Authorize]
    public async Task<IActionResult> Webcam([FromBody] WebcamRequest req, CancellationToken ct)
    {
        var camId = string.IsNullOrWhiteSpace(req.CameraId) ? "webcam-test" : req.CameraId;
        const string device = "Integrated Camera";

        var result = await _streams.StartAsync(camId, $"webcam:{device}", ct);
        if (!result.Success)
            return Fail(result.Error ?? "Failed to start webcam", 500);

        return Ok(new
        {
            success = true,
            message = $"Webcam stream started ({device})",
            cameraId = camId,
            hlsUrl = HlsUrl(camId),
        });
    }

    [HttpGet("status")]
    [Authorize]
    public IActionResult Status()
    {
        var streams = _streams.ActiveCameraIds
            .Select(camId => new { cameraId = camId, active = _streams.IsRunning(camId) });
        return Ok(new { success = true, streams });
    }

    /// <summary>
    /// Live MJPEG relay for a running camera. No auth: an &lt;img&gt; tag cannot send
    /// Authorization headers. Blocks until the client disconnects.
    /// </summary>
    [HttpGet("mjpeg/{cameraId}")]
    [AllowAnonymous]
    public async Task<IActionResult> Mjpeg(string cameraId)
    {
        if (!_streams.IsRunning(cameraId))
            return Fail($"No active stream for {cameraId}", 503);

        Response.StatusCode = 200;
        Response.ContentType = "multipart/x-mixed-replace; boundary=mjpegboundary";
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["Access-Control-Allow-Origin"] = "*";

        await _streams.AttachMjpegViewerAsync(cameraId, Response.Body, HttpContext.RequestAborted);
        return new EmptyResult();
    }

    /// <summary>
    /// Serves HLS playlists/segments from the streams root. Mirrors the Node
    /// express.static mount at /api/stream/hls with the same headers and content types.
    /// </summary>
    [HttpGet("hls/{cameraId}/{*file}")]
    [AllowAnonymous]
    public IActionResult Hls(string cameraId, string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return NotFound();

        var cameraDir = Path.GetFullPath(Path.Combine(_streamsRoot, cameraId));
        var fullPath = Path.GetFullPath(Path.Combine(cameraDir, file));

        // Guard against path traversal — the resolved file must stay under the camera dir.
        if (!fullPath.StartsWith(cameraDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return NotFound();

        if (!System.IO.File.Exists(fullPath))
            return NotFound();

        Response.Headers["Access-Control-Allow-Origin"] = "*";
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return File(stream, ContentTypeFor(fullPath));
    }

    private static string HlsUrl(string cameraId) => $"/api/stream/hls/{cameraId}/index.m3u8";

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".m3u8" => "application/vnd.apple.mpegurl",
        ".m4s" => "video/iso.segment",
        ".mp4" => "video/mp4",
        ".ts" => "video/mp2t",
        _ => "application/octet-stream",
    };
}
