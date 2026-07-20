using System.Diagnostics;

namespace AttackShield.Infrastructure.Services;

/// <summary>
/// FFmpeg argument construction and binary resolution. Arguments mirror the Node
/// streamController exactly: ultrafast/zerolatency H.264 into 0.5s HLS segments,
/// plus a second MJPEG output on stdout for low-latency live viewing.
/// </summary>
public sealed partial class FfmpegStreamManager
{
    private static List<string> BuildRtspArgs(string rtspUrl, string playlistPath, string segmentPath)
    {
        string decoded;
        try { decoded = Uri.UnescapeDataString(rtspUrl); }
        catch { decoded = rtspUrl; }

        return new List<string>
        {
            "-fflags", "nobuffer+discardcorrupt",
            "-flags", "low_delay",
            "-rtsp_transport", "tcp",
            "-timeout", "10000000",
            "-probesize", "32",
            "-analyzeduration", "0",
            "-i", decoded,
            // Output 1: HLS
            "-c:v", "libx264",
            "-preset", "ultrafast",
            "-tune", "zerolatency",
            "-g", "4",
            "-keyint_min", "4",
            "-sc_threshold", "0",
            "-r", "15",
            "-s", "640x480",
            "-b:v", "800k",
            "-an",
            "-f", "hls",
            "-hls_time", "0.5",
            "-hls_list_size", "2",
            "-hls_flags", "delete_segments+independent_segments+split_by_time",
            "-flush_packets", "1",
            "-hls_segment_filename", segmentPath,
            playlistPath,
            // Output 2: MJPEG → stdout
            "-map", "0:v",
            "-r", "15",
            "-s", "640x480",
            "-c:v", "mjpeg",
            "-q:v", "3",
            "-an",
            "-f", "mjpeg",
            "pipe:1",
        };
    }

    private static List<string> BuildWebcamArgs(string rtspUrl, string playlistPath, string segmentPath)
    {
        var deviceName = rtspUrl["webcam:".Length..].Trim();
        if (string.IsNullOrEmpty(deviceName))
            deviceName = "Integrated Camera";

        return new List<string>
        {
            "-f", "dshow",
            "-thread_queue_size", "512",
            "-i", $"video={deviceName}",
            // Output 1: HLS
            "-c:v", "libx264",
            "-preset", "ultrafast",
            "-tune", "zerolatency",
            "-g", "4",
            "-keyint_min", "4",
            "-sc_threshold", "0",
            "-r", "15",
            "-s", "640x480",
            "-b:v", "800k",
            "-an",
            "-f", "hls",
            "-hls_time", "0.5",
            "-hls_list_size", "2",
            "-hls_flags", "delete_segments+independent_segments+split_by_time",
            "-flush_packets", "1",
            "-hls_segment_filename", segmentPath,
            playlistPath,
            // Output 2: MJPEG → stdout
            "-map", "0:v",
            "-r", "15",
            "-s", "640x480",
            "-c:v", "mjpeg",
            "-q:v", "3",
            "-an",
            "-f", "mjpeg",
            "pipe:1",
        };
    }

    /// <summary>
    /// Resolves the ffmpeg binary: PATH first (via `where`/`which`), then the
    /// common winget install location on Windows, finally the bare name.
    /// </summary>
    private string ResolveFfmpegPath()
    {
        // 1. System PATH.
        try
        {
            var whichCmd = OperatingSystem.IsWindows() ? "where" : "which";
            var psi = new ProcessStartInfo(whichCmd, "ffmpeg")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is not null)
            {
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                var first = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first) && File.Exists(first))
                    return first;
            }
        }
        catch { /* not on PATH */ }

        // 2. winget fallback (Windows only).
        if (OperatingSystem.IsWindows())
        {
            var userProfile = Environment.GetEnvironmentVariable("USERPROFILE")
                              ?? Environment.GetEnvironmentVariable("HOME")
                              ?? string.Empty;
            var wingetDir = Path.Combine(userProfile, "AppData", "Local", "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetDir))
            {
                try
                {
                    foreach (var d in Directory.GetDirectories(wingetDir)
                                 .Where(d => Path.GetFileName(d).StartsWith("Gyan.FFmpeg", StringComparison.Ordinal)))
                    {
                        foreach (var sd in Directory.GetDirectories(d))
                        {
                            var candidate = Path.Combine(sd, "bin", "ffmpeg.exe");
                            if (File.Exists(candidate))
                                return candidate;
                        }
                    }
                }
                catch { /* ignore and fall through */ }
            }
        }

        // 3. Hope it's resolvable by name.
        return "ffmpeg";
    }
}
