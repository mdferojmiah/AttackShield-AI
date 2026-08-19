using System.Collections.Concurrent;
using AttackShield.Core.Interfaces;
using AttackShield.Infrastructure.Services.Streaming;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AttackShield.Infrastructure.Services;

/// <summary>
/// Runs one FFmpeg process per camera converting RTSP (or a local webcam) into
/// HLS segments on disk while also emitting an MJPEG stream on stdout that is
/// fanned out to connected HTTP viewers. Ported from the Node streamController,
/// keeping the same FFmpeg arguments, 0.5s segments and auto-restart behaviour.
/// Registered as a singleton so process state is shared across requests.
/// </summary>
public sealed partial class FfmpegStreamManager : IStreamManager, IDisposable
{
    private readonly ConcurrentDictionary<string, StreamEntry> _streams = new();
    private readonly object _lifecycleGate = new();
    private readonly ILogger<FfmpegStreamManager> _logger;
    private readonly string _streamsRoot;
    private readonly string _ffmpegPath;
    private volatile bool _disposed;

    public FfmpegStreamManager(
        IOptions<StreamOptions> options,
        IHostEnvironment env,
        ILogger<FfmpegStreamManager> logger)
    {
        _logger = logger;

        var opts = options.Value;
        _streamsRoot = Path.IsPathRooted(opts.StreamsRoot)
            ? opts.StreamsRoot
            : Path.Combine(env.ContentRootPath, opts.StreamsRoot);
        Directory.CreateDirectory(_streamsRoot);

        _ffmpegPath = string.IsNullOrWhiteSpace(opts.FfmpegPath)
            ? ResolveFfmpegPath()
            : opts.FfmpegPath;

        _logger.LogInformation("[Stream] Using FFmpeg at: {Path}", _ffmpegPath);
    }

    public IReadOnlyCollection<string> ActiveCameraIds => _streams.Keys.ToList();

    public bool IsRunning(string cameraId)
        => _streams.TryGetValue(cameraId, out var entry) && entry.Process is { HasExited: false };

    public Task<StreamResult> StartAsync(string cameraId, string rtspUrl, CancellationToken ct = default)
    {
        lock (_lifecycleGate)
        {
            if (IsRunning(cameraId))
            {
                _logger.LogInformation("[Stream] FFmpeg already running for camera {Camera}", cameraId);
                return Task.FromResult(StreamResult.Ok(PlaylistUrl(cameraId)));
            }

            try
            {
                StartFfmpeg(cameraId, rtspUrl);
                return Task.FromResult(StreamResult.Ok(PlaylistUrl(cameraId)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Stream] Failed to start FFmpeg for {Camera}", cameraId);
                _streams.TryRemove(cameraId, out _);
                return Task.FromResult(StreamResult.Fail(ex.Message));
            }
        }
    }

    public Task<StreamResult> StopAsync(string cameraId, CancellationToken ct = default)
    {
        StopFfmpeg(cameraId);
        return Task.FromResult(StreamResult.Ok());
    }

    public async Task<bool> AttachMjpegViewerAsync(string cameraId, Stream output, CancellationToken ct)
    {
        if (!_streams.TryGetValue(cameraId, out var entry) || entry.Process is null || entry.Process.HasExited)
            return false;

        var viewer = new MjpegViewer(output);
        entry.Viewers[viewer] = 0;

        // Link the client's request token with the stream's shutdown token so a
        // StopAsync (or an FFmpeg exit) ends this HTTP response immediately. Left
        // unlinked, the response stays open forever and the browser keeps burning
        // one of its ~6 connections per host, which is what made re-opening the
        // live feed appear to hang.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, entry.ShutdownToken);
        try
        {
            await Task.Delay(Timeout.Infinite, linked.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal client disconnect or stream shutdown.
        }
        finally
        {
            entry.Viewers.TryRemove(viewer, out _);
            viewer.Dead = true;
        }
        return true;
    }

    public Task StopAllAsync()
    {
        foreach (var id in _streams.Keys.ToList())
            StopFfmpeg(id);
        return Task.CompletedTask;
    }

    private string PlaylistUrl(string cameraId) => $"/streams/{cameraId}/index.m3u8";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAllAsync().GetAwaiter().GetResult();
    }
}
