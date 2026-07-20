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

    public Task DetectionOverlayAsync(object payload)
        => _hub.Clients.All.SendAsync("detection-overlay", payload);

    public Task WeaponDetectedAsync(object payload)
        => _hub.Clients.All.SendAsync("weapon-detected", payload);

    public Task NotificationCreatedAsync(object payload)
        => _hub.Clients.All.SendAsync("notification-created", payload);

    public Task AlertCreatedAsync(object payload)
        => _hub.Clients.All.SendAsync("alert-created", payload);
}
