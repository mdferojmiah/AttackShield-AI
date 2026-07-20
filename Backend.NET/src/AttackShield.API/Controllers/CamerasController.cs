using AttackShield.Core.DTOs;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

/// <summary>
/// Per-user cameras. Ported from the Node camerasController. The primary camera lives
/// on the user document (cctvName/rtspUrl/location); extras are embedded sub-documents
/// in <see cref="User.Cameras"/>. RTSP URLs are built via <see cref="IRtspUrlBuilder"/>
/// when only raw camera details are supplied. Authenticated.
/// </summary>
[Route("api/cameras")]
[Authorize]
public sealed class CamerasController : ApiControllerBase
{
    private readonly IUserRepository _users;
    private readonly IRtspUrlBuilder _rtsp;

    public CamerasController(IUserRepository users, IRtspUrlBuilder rtsp)
    {
        _users = users;
        _rtsp = rtsp;
    }

    [HttpGet]
    public async Task<IActionResult> GetCameras(CancellationToken ct)
    {
        var id = CurrentUserId;
        if (id is null) return Fail("Unauthorized", 401);

        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return Fail("User not found", 404);

        var primary = new CameraDto("primary", user.CctvName, user.RtspUrl, user.Location, null);

        var extras = user.Cameras.Select((cam, index) => new CameraDto(
            cam.Id ?? $"extra-{index}",
            cam.Name,
            cam.RtspUrl,
            cam.Location,
            cam.Brand));

        var cameras = new List<CameraDto> { primary };
        cameras.AddRange(extras);

        return Ok(new { success = true, data = cameras });
    }

    [HttpPost]
    public async Task<IActionResult> AddCamera([FromBody] AddCameraRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Location))
            return Fail("Camera name and location are required");

        var finalRtsp = req.RtspUrl;
        if (string.IsNullOrWhiteSpace(finalRtsp))
            finalRtsp = _rtsp.Build(req.CameraIp, req.CameraUsername, req.CameraPassword, req.CameraPort, req.CameraBrand, req.CameraPath);

        if (string.IsNullOrWhiteSpace(finalRtsp))
            return Fail("Unable to generate RTSP URL from provided camera details");

        var id = CurrentUserId;
        if (id is null) return Fail("Unauthorized", 401);

        var user = await _users.GetByIdAsync(id, ct);
        if (user is null) return Fail("User not found", 404);

        user.Cameras.Add(new EmbeddedCamera
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            Name = req.Name,
            RtspUrl = finalRtsp,
            Location = req.Location,
            Brand = req.CameraBrand,
        });
        user.UpdatedAt = DateTime.UtcNow;
        await _users.UpdateAsync(id, user, ct);

        return StatusCode(201, new { success = true, message = "Camera added successfully" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCamera(string id, CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (userId is null) return Fail("Unauthorized", 401);

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null) return Fail("User not found", 404);

        if (id == "primary")
        {
            // Clear the primary camera fields.
            user.RtspUrl = string.Empty;
            user.CctvName = string.Empty;
            user.UpdatedAt = DateTime.UtcNow;
            await _users.UpdateAsync(userId, user, ct);
            return Ok(new { success = true, message = "Primary camera removed" });
        }

        var cam = user.Cameras.FirstOrDefault(c => c.Id == id);
        if (cam is null)
            return Fail("Camera not found", 404);

        user.Cameras.Remove(cam);
        user.UpdatedAt = DateTime.UtcNow;
        await _users.UpdateAsync(userId, user, ct);

        return Ok(new { success = true, message = "Camera removed successfully" });
    }
}
