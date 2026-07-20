using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

/// <summary>
/// Notification endpoints. Ported from the Node notificationsController, which mixed
/// a buggy in-memory array with the Mongo model — this version is consistently
/// DB-backed via <see cref="INotificationRepository"/>. Notifications are returned
/// un-scoped (newest first), matching the original list behaviour. Most routes use
/// optional auth (public read); create requires authentication.
/// </summary>
[Route("api/notifications")]
public sealed class NotificationsController : ApiControllerBase
{
    private readonly INotificationRepository _notifications;

    // type → frontend icon name (mirrors the original iconMap).
    private static readonly Dictionary<string, string> IconMap = new()
    {
        ["weapon"] = "alert-circle",
        ["suspicious"] = "warning",
        ["vehicle"] = "car",
        ["loitering"] = "person",
        ["package"] = "cube",
        ["camera"] = "videocam-off",
        ["system"] = "settings",
    };

    public NotificationsController(INotificationRepository notifications)
    {
        _notifications = notifications;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var notifications = await _notifications.GetAllNewestAsync(ct);
        // Map _id → id and createdAt → time for frontend compatibility.
        var mapped = notifications.Select(Shape);
        return Ok(mapped);
    }

    [HttpGet("unread-count")]
    [AllowAnonymous]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        var count = await _notifications.CountAsync(n => !n.IsRead, ct);
        return Ok(new { success = true, count });
    }

    [HttpPut("read-all")]
    [AllowAnonymous]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        var all = await _notifications.GetAllNewestAsync(ct);
        foreach (var n in all.Where(n => !n.IsRead))
            await _notifications.MarkReadAsync(n.Id!, ct);

        return Ok(new { success = true, message = "All notifications marked as read" });
    }

    [HttpDelete("clear")]
    [AllowAnonymous]
    public async Task<IActionResult> ClearAll(CancellationToken ct)
    {
        var all = await _notifications.GetAllNewestAsync(ct);
        foreach (var n in all)
            await _notifications.DeleteAsync(n.Id!, ct);

        return Ok(new { success = true, message = "All notifications cleared" });
    }

    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetOne(string id, CancellationToken ct)
    {
        var notification = await _notifications.GetByIdAsync(id, ct);
        if (notification is null)
            return Fail("Notification not found", 404);

        return Ok(new { success = true, data = Shape(notification) });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateNotificationRequest req, CancellationToken ct)
    {
        var type = string.IsNullOrWhiteSpace(req.Type) ? "system" : req.Type;

        var notification = new Notification
        {
            Type = type,
            Title = req.Title ?? string.Empty,
            Description = req.Description ?? string.Empty,
            Icon = IconMap.TryGetValue(type, out var icon) ? icon : "notifications",
            Location = req.Location,
            UserId = req.UserId,
            IsRead = false,
        };
        await _notifications.InsertAsync(notification, ct);

        return StatusCode(201, new { success = true, data = Shape(notification) });
    }

    [HttpPut("{id}/read")]
    [AllowAnonymous]
    public async Task<IActionResult> MarkAsRead(string id, CancellationToken ct)
    {
        var notification = await _notifications.GetByIdAsync(id, ct);
        if (notification is null)
            return Fail("Notification not found", 404);

        await _notifications.MarkReadAsync(id, ct);
        notification.IsRead = true;

        return Ok(new { success = true, message = "Notification marked as read", data = Shape(notification) });
    }

    [HttpDelete("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var deleted = await _notifications.DeleteAsync(id, ct);
        if (!deleted)
            return Fail("Notification not found", 404);

        return Ok(new { success = true, message = "Notification deleted" });
    }

    /// <summary>Adds the frontend-facing id/time fields to a notification document.</summary>
    private static object Shape(Notification n) => new
    {
        id = n.Id,
        _id = n.Id,
        type = n.Type,
        title = n.Title,
        description = n.Description,
        icon = n.Icon,
        location = n.Location,
        userId = n.UserId,
        isRead = n.IsRead,
        createdAt = n.CreatedAt,
        time = n.CreatedAt.ToLocalTime().ToString("G"),
    };
}

/// <summary>Body for POST /api/notifications (system/ML-originated notifications).</summary>
public sealed record CreateNotificationRequest(
    string? Type,
    string? Title,
    string? Description,
    string? Location,
    string? UserId);
