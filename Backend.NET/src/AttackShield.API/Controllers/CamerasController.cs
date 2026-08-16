using AttackShield.Core.DTOs;
using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

[Authorize]
public sealed class CamerasController : ApiControllerBase
{
    private readonly IUserRepository _users;
    private readonly IRtspUrlBuilder _rtsp;
    private readonly IStreamManager _streams;

    public CamerasController(IUserRepository users, IRtspUrlBuilder rtsp, IStreamManager streams)
    {
        _users = users;
        _rtsp = rtsp;
        _streams = streams;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await GetUser(ct);
        if (user is null) return Fail("User not found", 404);

        var cameras = new List<CameraDto>();
        if (!string.IsNullOrWhiteSpace(user.RtspUrl))
            cameras.Add(new("primary", user.CctvName, user.RtspUrl, user.Location, null));
        cameras.AddRange(user.Cameras.Select((camera, index) =>
            new CameraDto(camera.Id ?? $"extra-{index}", camera.Name, camera.RtspUrl, camera.Location, camera.Brand)));
        return Ok(new { success = true, data = cameras });
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddCameraRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Location))
            return Fail("Camera name and location are required");

        var rtspUrl = request.RtspUrl ?? _rtsp.Build(request.CameraIp, request.CameraUsername,
            request.CameraPassword, request.CameraPort, request.CameraBrand, request.CameraPath);
        if (string.IsNullOrWhiteSpace(rtspUrl))
            return Fail("Unable to generate RTSP URL from provided camera details");

        var user = await GetUser(ct);
        if (user is null) return Fail("User not found", 404);
        user.Cameras.Add(new EmbeddedCamera
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            Name = request.Name,
            Location = request.Location,
            RtspUrl = rtspUrl,
            Brand = request.CameraBrand,
        });
        user.UpdatedAt = DateTime.UtcNow;
        await _users.UpdateAsync(user.Id!, user, ct);
        return StatusCode(201, new { success = true, message = "Camera added successfully" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var user = await GetUser(ct);
        if (user is null) return Fail("User not found", 404);

        if (id == "primary")
        {
            user.RtspUrl = string.Empty;
            user.CctvName = string.Empty;
            user.UpdatedAt = DateTime.UtcNow;
            await _users.UpdateAsync(user.Id!, user, ct);
            await _streams.StopAsync($"{user.Id}-primary", ct);
            return Ok(new { success = true, message = "Primary camera removed" });
        }

        var camera = user.Cameras.FirstOrDefault(c => c.Id == id);
        if (camera is null) return Fail("Camera not found", 404);
        user.Cameras.Remove(camera);
        user.UpdatedAt = DateTime.UtcNow;
        await _users.UpdateAsync(user.Id!, user, ct);
        await _streams.StopAsync($"{user.Id}-{id}", ct);
        return Ok(new { success = true, message = "Camera removed successfully" });
    }

    private Task<User?> GetUser(CancellationToken ct)
        => CurrentUserId is null ? Task.FromResult<User?>(null) : _users.GetByIdAsync(CurrentUserId, ct);
}
