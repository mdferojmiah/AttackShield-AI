using System.Collections.Concurrent;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace AttackShield.Api.Hubs;

/// <summary>
/// Real-time channel to the frontend. Replaces the original Socket.IO server.
///
/// Two responsibilities, both ported from the old server.js io.on('connection') block:
///  1. Clients invoke <see cref="StartDetection"/> to kick off AI processing; the hub
///     forwards the request to the FastAPI service (fire-and-forget).
///  2. Detection events are pushed back to clients from the detections controller via
///     <see cref="IDetectionBroadcaster"/>. Event names match the Socket.IO originals
///     ("detection-started", "weapon-detected", ...) so the client swap is drop-in.
///
/// The original tracked sockets per user and, when the last socket for a user
/// disconnected, told the AI service to stop. We keep that behaviour: connections
/// are counted per userId and StopDetection is sent when the count hits zero.
/// </summary>
public sealed class DetectionHub : Hub
{
    private readonly IAiServiceClient _ai;
    private readonly IConfiguration _config;
    private readonly ILogger<DetectionHub> _logger;

    // userId -> set of live connection ids. Static: shared across all hub instances
    // (SignalR creates one hub instance per invocation).
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> UserConnections = new();

    public DetectionHub(IAiServiceClient ai, IConfiguration config, ILogger<DetectionHub> logger)
    {
        _ai = ai;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Client -> server. Payload mirrors the old Socket.IO 'start-detection' message:
    /// { streamUrl, location, user, cameraName, cameraId }. For streams we serve
    /// ourselves (HLS under /streams/ or a "webcam:" source), we hand the AI service
    /// our MJPEG relay endpoint instead — OpenCV reads that continuous multipart
    /// stream natively, whereas rolling HLS segments get deleted before it can.
    /// </summary>
    public async Task StartDetection(StartDetectionPayload payload)
    {
        try
        {
            var aiStreamUrl = payload.StreamUrl;

            if (!string.IsNullOrEmpty(payload.CameraId)
                && aiStreamUrl is not null
                && (aiStreamUrl.Contains("/streams/") || aiStreamUrl.StartsWith("webcam:")))
            {
                var backendBase = _config["Backend:BaseUrl"] ?? "http://localhost:5000";
                aiStreamUrl = $"{backendBase}/api/stream/mjpeg/{payload.CameraId}";
            }

            // Track this connection against the user so we can stop detection when the
            // user's last connection drops (see OnDisconnectedAsync).
            if (!string.IsNullOrEmpty(payload.User))
            {
                var set = UserConnections.GetOrAdd(payload.User, _ => new ConcurrentDictionary<string, byte>());
                set[Context.ConnectionId] = 1;
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupForUser(payload.User));
            }

            // Ack immediately — don't block the caller on AI model cold-start.
            await Clients.Caller.SendAsync("detection-started",
                new { success = true, message = "Detection request sent to AI service" });

            // Fire-and-forget forward to FastAPI. Errors are logged, not surfaced.
            _ = ForwardStartAsync(aiStreamUrl, payload.Location, payload.User);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI] Error in start-detection handler");
            await Clients.Caller.SendAsync("detection-started",
                new { success = false, error = ex.Message });
        }
    }

    private async Task ForwardStartAsync(string? rtspUrl, string? location, string? userId)
    {
        _logger.LogInformation("[AI] Forwarding detection request to AI service: {Url}", rtspUrl);
        var result = await _ai.StartDetectionAsync(rtspUrl ?? string.Empty, location, userId);
        if (!result.Success)
            _logger.LogError("[AI] Error forwarding to AI service: {Error}", result.Error);
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("User connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("User disconnected: {ConnectionId}", Context.ConnectionId);

        // Find which user this connection belonged to and drop it. When a user has
        // no remaining connections, tell the AI service to stop (matches server.js).
        foreach (var (userId, connections) in UserConnections)
        {
            if (connections.TryRemove(Context.ConnectionId, out _) && connections.IsEmpty)
            {
                UserConnections.TryRemove(userId, out _);
                _logger.LogInformation("[Socket] All connections for user {User} gone. Stopping detection.", userId);
                var stop = await _ai.StopDetectionAsync();
                if (!stop.Success)
                    _logger.LogError("[AI] Error stopping detection: {Error}", stop.Error);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    internal static string GroupForUser(string userId) => $"user:{userId}";
}

/// <summary>Payload for the client-invoked <see cref="DetectionHub.StartDetection"/>.</summary>
public sealed record StartDetectionPayload(
    string? StreamUrl,
    string? Location,
    string? User,
    string? CameraName,
    string? CameraId);
