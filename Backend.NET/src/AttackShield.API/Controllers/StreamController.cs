using AttackShield.Core.DTOs;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

[Authorize]
public sealed class StreamController : ApiControllerBase
{
    private readonly IStreamManager _streams;
    private readonly IUserRepository _users;

    public StreamController(IStreamManager streams, IUserRepository users)
    {
        _streams = streams;
        _users = users;
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromBody] StreamStartRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CameraId)) return Fail("cameraId is required");
        var user = await GetUser(ct);
        if (user is null) return Fail("User not found", 404);
        var rtspUrl = CameraUrl(user, request.CameraId);
        if (string.IsNullOrWhiteSpace(rtspUrl)) return Fail("Camera not found", 404);
        var result = await _streams.StartAsync(StreamId(request.CameraId), rtspUrl, ct);
        if (!result.Success) return Fail(result.Error ?? "Failed to start stream", 500);
        return Ok(new { success = true, message = $"Stream started for camera {request.CameraId}", hlsUrl = HlsUrl(StreamId(request.CameraId)) });
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop([FromBody] StreamStopRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CameraId)) return Fail("cameraId is required");
        await _streams.StopAsync(StreamId(request.CameraId), ct);
        return Ok(new { success = true, message = $"Stream stopped for camera {request.CameraId}" });
    }

    [HttpPost("start-all")]
    public async Task<IActionResult> StartAll(CancellationToken ct)
    {
        var user = await GetUser(ct);
        if (user is null) return Fail("User not found", 404);
        var started = new List<object>();
        if (!string.IsNullOrWhiteSpace(user.RtspUrl))
            await StartOne("primary", user.RtspUrl, started, ct);
        foreach (var (camera, index) in user.Cameras.Select((camera, index) => (camera, index)))
            if (!string.IsNullOrWhiteSpace(camera.RtspUrl))
                await StartOne(camera.Id ?? $"extra-{index}", camera.RtspUrl, started, ct);
        return Ok(new { success = true, message = $"Started {started.Count} stream(s)", streams = started });
    }

    [HttpPost("stop-all")]
    public async Task<IActionResult> StopAll()
    {
        var stopped = OwnedStreamIds().ToArray();
        foreach (var streamId in stopped)
            await _streams.StopAsync(streamId);
        return Ok(new { success = true, message = $"Stopped {stopped.Length} stream(s)" });
    }

    [HttpGet("status")]
    public IActionResult Status()
        => Ok(new { success = true, streams = OwnedStreamIds().Select(id => new { cameraId = CameraId(id), active = _streams.IsRunning(id) }) });

    [HttpPost("webcam")]
    public async Task<IActionResult> Webcam([FromBody] WebcamRequest request, CancellationToken ct)
    {
        var cameraId = string.IsNullOrWhiteSpace(request.CameraId) ? "webcam-test" : request.CameraId;
        var deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? "Integrated Camera" : request.DeviceName;
        var result = await _streams.StartAsync(StreamId(cameraId), $"webcam:{deviceName}", ct);
        if (!result.Success) return Fail(result.Error ?? "Failed to start webcam", 500);
        return Ok(new { success = true, message = $"Webcam stream started ({deviceName})", cameraId, hlsUrl = HlsUrl(StreamId(cameraId)) });
    }

    [HttpGet("mjpeg/{cameraId}")]
    public async Task<IActionResult> Mjpeg(string cameraId, CancellationToken ct)
    {
        var streamId = StreamId(cameraId);
        if (!_streams.IsRunning(streamId)) return Fail($"No active stream for {cameraId}", 503);
        Response.ContentType = "multipart/x-mixed-replace; boundary=mjpegboundary";
        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        await _streams.AttachMjpegViewerAsync(streamId, Response.Body, ct);
        return new EmptyResult();
    }

    private static string HlsUrl(string cameraId) => $"/api/stream/hls/{cameraId}/index.m3u8";

    private async Task StartOne(string id, string rtspUrl, List<object> started, CancellationToken ct)
    {
        var streamId = StreamId(id);
        var result = await _streams.StartAsync(streamId, rtspUrl, ct);
        if (result.Success) started.Add(new { cameraId = id, hlsUrl = HlsUrl(streamId) });
    }

    private string StreamId(string cameraId) => $"{CurrentUserId}-{cameraId}";

    private IEnumerable<string> OwnedStreamIds()
        => _streams.ActiveCameraIds.Where(id => id.StartsWith($"{CurrentUserId}-", StringComparison.Ordinal));

    private string CameraId(string streamId) => streamId[$"{CurrentUserId}-".Length..];

    private static string? CameraUrl(User user, string cameraId)
    {
        if (cameraId == "primary") return user.RtspUrl;
        return user.Cameras.FirstOrDefault(camera => camera.Id == cameraId)?.RtspUrl;
    }

    private Task<User?> GetUser(CancellationToken ct)
        => CurrentUserId is null ? Task.FromResult<User?>(null) : _users.GetByIdAsync(CurrentUserId, ct);
}
