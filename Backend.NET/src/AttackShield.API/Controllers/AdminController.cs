using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

[Authorize(Roles = "admin")]
public sealed class AdminController : ApiControllerBase
{
    private readonly IUserRepository _users;
    private readonly IDetectionRepository _detections;

    public AdminController(IUserRepository users, IDetectionRepository detections)
    {
        _users = users;
        _detections = detections;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        var usersTask = _users.GetAllAsync(ct);
        var detectionsTask = _detections.GetRecentAsync(null, 50, ct);
        await Task.WhenAll(usersTask, detectionsTask);

        var users = await usersTask;
        var userNames = users
            .Where(user => user.Id is not null)
            .ToDictionary(user => user.Id!, user => user.Name);

        var userRows = users.Select(user => new
        {
            id = user.Id,
            user.Name,
            user.Email,
            user.Phone,
            user.Role,
            user.IsActive,
            user.LastLogin,
            user.CreatedAt,
            cameras = PrimaryCamera(user).Concat(user.Cameras.Select(camera => new
            {
                id = camera.Id,
                name = camera.Name,
                location = camera.Location,
                brand = camera.Brand,
                isPrimary = false
            }))
        });

        var recentActions = (await detectionsTask).Select(detection => new
        {
            id = detection.Id,
            userId = detection.UserId,
            userName = detection.UserId is not null && userNames.TryGetValue(detection.UserId, out var name)
                ? name
                : "System",
            type = detection.DetectionType,
            label = detection.WeaponType,
            detection.Location,
            detection.CameraName,
            detection.Confidence,
            detection.CreatedAt
        });

        return Ok(new
        {
            success = true,
            data = new
            {
                totalUsers = users.Count,
                activeUsers = users.Count(user => user.IsActive),
                totalCameras = users.Sum(user => user.Cameras.Count + (string.IsNullOrWhiteSpace(user.RtspUrl) ? 0 : 1)),
                users = userRows,
                recentActions
            }
        });
    }

    private static IEnumerable<object> PrimaryCamera(AttackShield.Core.Entities.User user)
    {
        if (string.IsNullOrWhiteSpace(user.RtspUrl)) yield break;
        yield return new
        {
            id = "primary",
            name = user.CctvName ?? "Primary Camera",
            location = user.Location,
            brand = (string?)null,
            isPrimary = true
        };
    }
}