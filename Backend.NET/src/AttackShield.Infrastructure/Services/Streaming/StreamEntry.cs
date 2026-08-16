using System.Collections.Concurrent;
using System.Diagnostics;

namespace AttackShield.Infrastructure.Services.Streaming;

/// <summary>
/// Live state for one camera's FFmpeg process plus its connected MJPEG viewers.
/// Viewers are keyed in a concurrent dictionary (used as a set) so they can be
/// added/removed from request threads while the stdout pump iterates them.
/// </summary>
internal sealed class StreamEntry
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
}
