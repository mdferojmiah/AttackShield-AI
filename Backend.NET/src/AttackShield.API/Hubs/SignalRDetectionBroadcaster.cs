using Microsoft.AspNetCore.SignalR;

namespace AttackShield.Api.Hubs;

/// <summary>
/// SignalR implementation of <see cref="IDetectionBroadcaster"/>. Broadcasts to all
/// connected clients, exactly like the original io.emit(...) calls. Event names are
/// preserved verbatim so existing frontend handlers keep working.
/// </summary>
public sealed class SignalRDetectionBroadcaster : IDetectionBroadcaster
{
    private readonly IHubContext<DetectionHub> _hub;

    public SignalRDetectionBroadcaster(IHubContext<DetectionHub> hub) => _hub = hub;

    public Task DetectionOverlayAsync(object payload, string? userId)
        => SendAsync("detection-overlay", payload, userId);

    public Task WeaponDetectedAsync(object payload, string? userId)
        => SendAsync("weapon-detected", payload, userId);

    public Task NotificationCreatedAsync(object payload, string? userId)
        => SendAsync("notification-created", payload, userId);

    public Task AlertCreatedAsync(object payload, string? userId)
        => SendAsync("alert-created", payload, userId, includeAuthorities: true);

    // Only the recipient cares about their own throttle, so this one skips the admin group.
    public Task EmailCooldownAsync(object payload, string? userId)
        => string.IsNullOrWhiteSpace(userId)
            ? Task.CompletedTask
            : _hub.Clients.Group(DetectionHub.GroupForUser(userId)).SendAsync("email-cooldown", payload);

    private Task SendAsync(string eventName, object payload, string? userId, bool includeAuthorities = false)
    {
        var groups = new List<string> { DetectionHub.GroupForRole("admin") };
        if (!string.IsNullOrWhiteSpace(userId)) groups.Add(DetectionHub.GroupForUser(userId));
        if (includeAuthorities)
        {
            groups.Add(DetectionHub.GroupForRole("authority"));
            groups.Add(DetectionHub.GroupForRole("senior_authority"));
        }
        return _hub.Clients.Groups(groups.Distinct()).SendAsync(eventName, payload);
    }
}
