using System.Collections.Concurrent;
using AttackShield.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
/// Connections are tracked per user for group membership and diagnostics. A
/// transient browser or network disconnect must not stop camera jobs; the Live
/// Feed page explicitly owns detection shutdown.
/// </summary>
[Authorize]
public sealed class DetectionHub : Hub
{
    private readonly IAiServiceClient _ai;
    private readonly IUserRepository _users;
    private readonly IConfiguration _config;
    private readonly ILogger<DetectionHub> _logger;

    // userId -> set of live connection ids. Static: shared across all hub instances
    // (SignalR creates one hub instance per invocation).
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> UserConnections = new();

    public DetectionHub(IAiServiceClient ai, IUserRepository users, IConfiguration config, ILogger<DetectionHub> logger)
    {
        _ai = ai;
        _users = users;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Client -> server. Payload mirrors the old Socket.IO 'start-detection' message:
    /// { streamUrl, location, user, cameraName, cameraId }. For streams we serve
    /// ourselves (HLS under /streams/ or a "webcam:" source), we pass the public HLS
    /// playlist to the AI service. FastAPI uses FFmpeg for HLS, while browser-facing
    /// MJPEG remains protected and is not reused as an internal service endpoint.
    /// </summary>
    public async Task StartDetection(StartDetectionPayload payload)
    {
        try
        {
            var aiStreamUrl = payload.StreamUrl;
            var userId = Context.User?.FindFirst("id")?.Value;

            if (!string.IsNullOrEmpty(payload.CameraId)
                && aiStreamUrl is not null
                && (aiStreamUrl.Contains("/streams/") || aiStreamUrl.StartsWith("webcam:")))
            {
                var backendBase = _config["Backend:BaseUrl"] ?? "http://localhost:5217";
                aiStreamUrl = $"{backendBase}/streams/{userId}-{payload.CameraId}/index.m3u8";
            }

            // Track this connection against the user for group membership.
            if (!string.IsNullOrEmpty(userId))
            {
                var set = UserConnections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
                set[Context.ConnectionId] = 1;
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupForUser(userId));
            }

            // Ack immediately — don't block the caller on AI model cold-start.
            await Clients.Caller.SendAsync("detection-started",
                new { success = true, message = "Detection request sent to AI service" });

            // Fire-and-forget forward to FastAPI. Errors are logged, not surfaced.
            _ = ForwardStartAsync(
                aiStreamUrl,
                payload.Location,
                userId,
                payload.CameraId,
                payload.CameraName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AI] Error in start-detection handler");
            await Clients.Caller.SendAsync("detection-started",
                new { success = false, error = ex.Message });
        }
    }

    private async Task ForwardStartAsync(
        string? rtspUrl,
        string? location,
        string? userId,
        string? cameraId,
        string? cameraName)
    {
        _logger.LogInformation("[AI] Forwarding detection request to AI service: {Url}", rtspUrl);
        var user = string.IsNullOrEmpty(userId) ? null : await _users.GetByIdAsync(userId);
        var hitList = user?.HitList
            .Where(entry => entry.Id is not null)
            .Select(entry => new HitListReference(entry.Id!, entry.Name, entry.ImageUrl))
            .ToList() ?? [];
        var result = await _ai.StartDetectionAsync(
            rtspUrl ?? string.Empty,
            location,
            userId,
            cameraId,
            cameraName,
            hitList);
        if (!result.Success)
            _logger.LogError("[AI] Error forwarding to AI service: {Error}", result.Error);
    }

    public async Task StopDetection(object? payload = null)
    {
        string? cameraId = null;
        if (payload is not null)
        {
            try
            {
                cameraId = System.Text.Json.JsonSerializer
                    .Deserialize<StopDetectionPayload>(
                        payload.ToString() ?? string.Empty,
                        new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        })?.CameraId;
            }
            catch (System.Text.Json.JsonException)
            {
                _logger.LogDebug("Ignoring malformed stop-detection payload");
            }
        }

        var result = await _ai.StopDetectionAsync(cameraId);
        if (!result.Success)
            throw new HubException(result.Error ?? "Could not stop AI detection");
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("User connected: {ConnectionId}", Context.ConnectionId);
        var userId = Context.User?.FindFirst("id")?.Value;
        var role = Context.User?.FindFirst("role")?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForUser(userId));
        if (!string.IsNullOrWhiteSpace(role))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForRole(role));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("User disconnected: {ConnectionId}", Context.ConnectionId);

        // Remove the connection from the tracked set. SignalR automatic reconnects
        // and page transitions can briefly disconnect without ending detection.
        foreach (var (userId, connections) in UserConnections)
        {
            if (connections.TryRemove(Context.ConnectionId, out _) && connections.IsEmpty)
            {
                UserConnections.TryRemove(userId, out _);
                _logger.LogInformation("[Socket] All connections for user {User} gone.", userId);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    internal static string GroupForUser(string userId) => $"user:{userId}";
    internal static string GroupForRole(string role) => $"role:{role}";
}

/// <summary>Payload for the client-invoked <see cref="DetectionHub.StartDetection"/>.</summary>
public sealed record StartDetectionPayload(
    string? StreamUrl,
    string? Location,
    string? User,
    string? CameraName,
    string? CameraId);

public sealed record StopDetectionPayload(string? CameraId);
