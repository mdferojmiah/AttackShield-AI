namespace AttackShield.Api.Hubs;

/// <summary>
/// Fans real-time events out to connected clients. Event/method names mirror the
/// original Socket.IO events one-to-one so the frontend swaps transports without
/// changing its event handlers.
/// </summary>
public interface IDetectionBroadcaster
{
    Task DetectionOverlayAsync(object payload);
    Task WeaponDetectedAsync(object payload);
    Task NotificationCreatedAsync(object payload);
    Task AlertCreatedAsync(object payload);
}
