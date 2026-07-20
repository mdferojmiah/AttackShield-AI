using System.Diagnostics;
using AttackShield.Infrastructure.Services.Streaming;
using Microsoft.Extensions.Logging;

namespace AttackShield.Infrastructure.Services;

/// <summary>
/// Process lifecycle, FFmpeg argument construction, and the MJPEG stdout pump.
/// Kept in a separate partial file from the public IStreamManager surface.
/// </summary>
public sealed partial class FfmpegStreamManager
{
    private static readonly byte[] MjpegHeaderPrefix = System.Text.Encoding.ASCII.GetBytes(
        "--mjpegboundary\r\nContent-Type: image/jpeg\r\nContent-Length: ");
    private static readonly byte[] MjpegHeaderSuffix = System.Text.Encoding.ASCII.GetBytes("\r\n\r\n");

    private string EnsureCameraDir(string cameraId)
    {
        var dir = Path.Combine(_streamsRoot, cameraId);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        else
        {
            // Remove stale HLS artefacts from a previous session.
            try
            {
                foreach (var f in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is ".m3u8" or ".ts" or ".m4s" or ".tmp")
                        File.Delete(f);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning("[Stream] Could not clean dir for {Camera}: {Msg}", cameraId, e.Message);
            }
        }
        return dir;
    }

    private void StartFfmpeg(string cameraId, string rtspUrl)
    {
        var outputDir = EnsureCameraDir(cameraId);
        var playlistPath = Path.Combine(outputDir, "index.m3u8");
        var segmentPath = Path.Combine(outputDir, "seg_%03d.ts");

        var isWebcam = rtspUrl.StartsWith("webcam:", StringComparison.OrdinalIgnoreCase);
        var args = isWebcam
            ? BuildWebcamArgs(rtspUrl, playlistPath, segmentPath)
            : BuildRtspArgs(rtspUrl, playlistPath, segmentPath);

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var entry = new StreamEntry(process, rtspUrl);
        _streams[cameraId] = entry;

        process.Exited += (_, _) =>
        {
            int code = SafeExitCode(process);
            _logger.LogInformation("[Stream] FFmpeg for camera {Camera} exited with code {Code}", cameraId, code);
            var wasStreaming = entry.HasOutput;
            _streams.TryRemove(cameraId, out _);

            // Auto-restart when it was streaming and did not exit cleanly (0),
            // unless a caller intentionally stopped it.
            if (wasStreaming && code != 0 && !entry.StopRequested && !_disposed)
            {
                _logger.LogInformation("[Stream][{Camera}] Unexpected exit — auto-restarting in 3s", cameraId);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    if (!_streams.ContainsKey(cameraId) && !_disposed)
                    {
                        try { StartFfmpeg(cameraId, rtspUrl); }
                        catch (Exception ex) { _logger.LogError(ex, "[Stream] Auto-restart failed for {Camera}", cameraId); }
                    }
                });
            }
        };

        process.Start();

        // Pump stderr (FFmpeg logs everything there) to detect "Output #0" / errors.
        _ = Task.Run(() => PumpStderrAsync(cameraId, entry));
        // Pump stdout MJPEG frames to viewers.
        _ = Task.Run(() => PumpMjpegAsync(cameraId, entry));

        // Auto-kill if no output within 20s (camera unreachable).
        _ = Task.Run(async () =>
        {
            await Task.Delay(20000);
            if (!entry.HasOutput && !process.HasExited)
            {
                _logger.LogError("[Stream][{Camera}] Timeout: no output after 20s - killing FFmpeg", cameraId);
                TryKill(process);
            }
        });
    }

    private void StopFfmpeg(string cameraId)
    {
        if (!_streams.TryRemove(cameraId, out var entry))
            return;

        entry.StopRequested = true;
        foreach (var v in entry.Viewers.Keys)
            v.Dead = true;

        if (entry.Process is { } p)
        {
            _logger.LogInformation("[Stream] Stopping FFmpeg for camera {Camera}", cameraId);
            TryKill(p);
        }
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; }
        catch { return -1; }
    }

    private static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
        catch { /* already gone */ }
    }

    private async Task PumpStderrAsync(string cameraId, StreamEntry entry)
    {
        try
        {
            var reader = entry.Process.StandardError;
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (line.StartsWith("frame=", StringComparison.Ordinal) || line.Contains("for writing"))
                    continue;

                if (line.Contains("Output #0", StringComparison.Ordinal))
                {
                    entry.HasOutput = true;
                    _logger.LogInformation("[Stream][{Camera}] HLS output started", cameraId);
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(
                    line, "error|failed|refused|timeout|unauthorized|denied|no route",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    _logger.LogError("[Stream][{Camera}] FFmpeg error: {Msg}", cameraId, line.Trim());
                }
            }
        }
        catch { /* process ended */ }
    }

    /// <summary>
    /// Reads the raw MJPEG byte stream from stdout, extracts complete JPEG frames
    /// (SOI 0xFFD8 .. EOI 0xFFD9) and fans each one out to connected viewers as a
    /// multipart/x-mixed-replace chunk.
    /// </summary>
    private async Task PumpMjpegAsync(string cameraId, StreamEntry entry)
    {
        var stdout = entry.Process.StandardOutput.BaseStream;
        var buffer = new byte[64 * 1024];
        var acc = new List<byte>(1 << 20);

        try
        {
            int read;
            while ((read = await stdout.ReadAsync(buffer)) > 0)
            {
                for (int i = 0; i < read; i++)
                    acc.Add(buffer[i]);

                while (true)
                {
                    int start = IndexOfMarker(acc, 0xFF, 0xD8, 0);
                    if (start < 0)
                    {
                        acc.Clear();
                        break;
                    }
                    int end = IndexOfMarker(acc, 0xFF, 0xD9, start + 2);
                    if (end < 0)
                    {
                        if (start > 0)
                            acc.RemoveRange(0, start);
                        break;
                    }

                    int frameLen = end + 2 - start;
                    var frame = new byte[frameLen];
                    acc.CopyTo(start, frame, 0, frameLen);
                    acc.RemoveRange(0, end + 2);

                    await BroadcastFrameAsync(entry, frame);
                }
            }
        }
        catch { /* process ended or stdout closed */ }
    }

    private static async Task BroadcastFrameAsync(StreamEntry entry, byte[] frame)
    {
        if (entry.Viewers.IsEmpty)
            return;

        var lenBytes = System.Text.Encoding.ASCII.GetBytes(frame.Length.ToString());
        var header = new byte[MjpegHeaderPrefix.Length + lenBytes.Length + MjpegHeaderSuffix.Length];
        Buffer.BlockCopy(MjpegHeaderPrefix, 0, header, 0, MjpegHeaderPrefix.Length);
        Buffer.BlockCopy(lenBytes, 0, header, MjpegHeaderPrefix.Length, lenBytes.Length);
        Buffer.BlockCopy(MjpegHeaderSuffix, 0, header, MjpegHeaderPrefix.Length + lenBytes.Length, MjpegHeaderSuffix.Length);

        foreach (var viewer in entry.Viewers.Keys)
        {
            await viewer.WriteFrameAsync(header, frame);
            if (viewer.Dead)
                entry.Viewers.TryRemove(viewer, out _);
        }
    }

    private static int IndexOfMarker(List<byte> data, byte b0, byte b1, int from)
    {
        for (int i = Math.Max(0, from); i < data.Count - 1; i++)
        {
            if (data[i] == b0 && data[i + 1] == b1)
                return i;
        }
        return -1;
    }
}
