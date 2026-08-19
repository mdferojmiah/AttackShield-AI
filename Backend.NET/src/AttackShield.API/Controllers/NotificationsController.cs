using AttackShield.Core.Entities;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AttackShield.Api.Controllers;

[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ApiControllerBase
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 50;

    private readonly INotificationRepository _notifications;

    public NotificationsController(INotificationRepository notifications) => _notifications = notifications;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? limit, [FromQuery] string? cursor, CancellationToken ct)
    {
        DateTime? before = null;
        string? beforeId = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!NotificationCursor.TryDecode(cursor, out before, out beforeId))
                return Fail("Invalid cursor");
        }

        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var page = await _notifications.GetPageAsync(OwnerId, before, beforeId, size, ct);
        var last = page.Items.Count > 0 ? page.Items[^1] : null;

        return Ok(new
        {
            success = true,
            items = page.Items.Select(MapSummary),
            hasMore = page.HasMore,
            nextCursor = page.HasMore && last is not null ? NotificationCursor.Encode(last) : null,
        });
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        var count = (await _notifications.GetAllNewestAsync(OwnerId, ct)).Count(n => !n.IsRead);
        return Ok(new { success = true, count });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var notification = await _notifications.GetByIdAsync(id, ct);
        return notification is null || !CanAccess(notification)
            ? Fail("Notification not found", 404)
            : Ok(new { success = true, data = Map(notification) });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateNotificationRequest request, CancellationToken ct)
    {
        var notification = new Notification
        {
            Type = string.IsNullOrWhiteSpace(request.Type) ? "system" : request.Type,
            Title = request.Title ?? string.Empty,
            Description = request.Description ?? string.Empty,
            Location = request.Location,
            ImageUrl = request.ImageUrl,
            UserId = CurrentUserRole == "admin" ? request.UserId ?? CurrentUserId : CurrentUserId,
            Icon = IconFor(request.Type),
        };
        await _notifications.InsertAsync(notification, ct);
        return Created($"/api/notifications/{notification.Id}", new { success = true, data = Map(notification) });
    }

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkRead(string id, CancellationToken ct)
    {
        var notification = await _notifications.GetByIdAsync(id, ct);
        if (notification is null || !CanAccess(notification)) return Fail("Notification not found", 404);
        notification.IsRead = true;
        await _notifications.UpdateAsync(id, notification, ct);
        return Ok(new { success = true, message = "Notification marked as read", data = Map(notification) });
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        foreach (var notification in await _notifications.GetAllNewestAsync(OwnerId, ct))
        {
            if (!notification.IsRead && notification.Id is not null)
            {
                notification.IsRead = true;
                await _notifications.UpdateAsync(notification.Id, notification, ct);
            }
        }
        return Ok(new { success = true, message = "All notifications marked as read" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var notification = await _notifications.GetByIdAsync(id, ct);
        if (notification is null || !CanAccess(notification)) return Fail("Notification not found", 404);
        return await _notifications.DeleteAsync(id, ct)
            ? Ok(new { success = true, message = "Notification deleted" })
            : Fail("Notification not found", 404);
    }

    [HttpDelete("clear")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        foreach (var notification in await _notifications.GetAllNewestAsync(OwnerId, ct))
        {
            if (notification.Id is not null)
                await _notifications.DeleteAsync(notification.Id, ct);
        }
        return Ok(new { success = true, message = "All notifications cleared" });
    }

    private string? OwnerId => CurrentUserRole == "admin" ? null : CurrentUserId;

    private bool CanAccess(Notification notification)
        => CurrentUserRole == "admin" || notification.UserId == CurrentUserId;

    private static object Map(Notification notification) => new
    {
        _id = notification.Id,
        id = notification.Id,
        notification.Type,
        notification.Title,
        notification.Description,
        notification.Icon,
        notification.Location,
        notification.ImageUrl,
        notification.IsRead,
        notification.CreatedAt,
        time = notification.CreatedAt.ToLocalTime().ToString("G"),
    };

    // ImageUrl is an inline base64 JPEG, so a page of these dwarfs everything else on the wire.
    // List rows omit it; the details view fetches the single notification to get it.
    private static object MapSummary(Notification notification) => new
    {
        _id = notification.Id,
        id = notification.Id,
        notification.Type,
        notification.Title,
        notification.Description,
        notification.Icon,
        notification.Location,
        notification.IsRead,
        notification.CreatedAt,
        time = notification.CreatedAt.ToLocalTime().ToString("G"),
    };

    private static string IconFor(string? type) => type switch
    {
        "weapon" => "alert-circle",
        "suspicious" => "warning",
        "vehicle" => "car",
        "loitering" => "person",
        "package" => "cube",
        "camera" => "videocam-off",
        "system" => "settings",
        _ => "notifications",
    };

    public sealed record CreateNotificationRequest(
        string? Type,
        string? Title,
        string? Description,
        string? Location,
        string? ImageUrl,
        string? UserId);
}
