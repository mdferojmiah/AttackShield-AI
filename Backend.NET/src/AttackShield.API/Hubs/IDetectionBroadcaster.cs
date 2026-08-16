namespace AttackShield.Api.Hubs;

/// <summary>
/// Fans real-time events out to connected clients. Event/method names mirror the
/// original Socket.IO events one-to-one so the frontend swaps transports without
/// changing its event handlers.
/// </summary>
public interface IDetectionBroadcaster
{
    Task DetectionOverlayAsync(object payload, string? userId);
    Task WeaponDetectedAsync(object payload, string? userId);
    Task NotificationCreatedAsync(object payload, string? userId);
    Task AlertCreatedAsync(object payload, string? userId);
}
