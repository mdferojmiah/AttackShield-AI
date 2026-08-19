using System.Collections.Concurrent;
using System.Diagnostics;

namespace AttackShield.Infrastructure.Services.Streaming;

/// <summary>
/// Live state for one camera's FFmpeg process plus its connected MJPEG viewers.
/// Viewers are keyed in a concurrent dictionary (used as a set) so they can be
/// added/removed from request threads while the stdout pump iterates them.
/// </summary>
internal sealed class StreamEntry : IDisposable
{
    public StreamEntry(Process process, string rtspUrl)
    {
        Process = process;
        RtspUrl = rtspUrl;
    }

    public Process Process { get; }
    public string RtspUrl { get; }

    /// <summary>True once FFmpeg has confirmed it opened its output ("Output #0").</summary>
    public volatile bool HasOutput;

    /// <summary>Set when a caller intentionally stopped the stream (suppresses auto-restart).</summary>
    public volatile bool StopRequested;

    public ConcurrentDictionary<MjpegViewer, byte> Viewers { get; } = new();

    /// <summary>
    /// Cancelled when this stream shuts down. Attached MJPEG viewers link their
    /// request token to this one so stopping the camera completes their HTTP
    /// response instead of leaving it hanging until the browser gives up.
    /// </summary>
    private readonly CancellationTokenSource _shutdown = new();

    public CancellationToken ShutdownToken
        => _shutdown.IsCancellationRequested ? new CancellationToken(true) : _shutdown.Token;

    public void SignalShutdown()
    {
        try { _shutdown.Cancel(); }
        catch (ObjectDisposedException) { /* already torn down */ }
    }

    public void Dispose()
    {
        SignalShutdown();
        _shutdown.Dispose();
    }
}
