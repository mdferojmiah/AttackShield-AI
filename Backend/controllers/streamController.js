/**
 * Stream Controller
 * Manages RTSP → HLS conversion using FFmpeg
 *
 * Each camera gets its own FFmpeg process that converts the RTSP stream
 * into HLS segments (.ts) and a playlist (.m3u8) served as static files.
 */

const { spawn } = require('child_process');
const { execSync } = require('child_process');
const path = require('path');
const fs = require('fs');
const User = require('../models/User');

// Track active FFmpeg processes: cameraId → { process, rtspUrl }
const activeStreams = new Map();

// Directory where HLS segments are written
const STREAMS_DIR = path.join(__dirname, '..', 'public', 'streams');

/**
 * Resolve the full path to FFmpeg binary.
 * Checks PATH first, then falls back to the common winget install location.
 */
function resolveFFmpegPath() {
  // 1. Try the system PATH (works if terminal was restarted after install)
  try {
    const out = execSync('where ffmpeg', { encoding: 'utf8', timeout: 5000 }).trim();
    if (out) return out.split(/\r?\n/)[0];
  } catch (_) { /* not on PATH */ }

  // 2. Fallback: common winget install location
  const userProfile = process.env.USERPROFILE || process.env.HOME || '';
  const wingetDir = path.join(
    userProfile,
    'AppData', 'Local', 'Microsoft', 'WinGet', 'Packages'
  );
  if (fs.existsSync(wingetDir)) {
    const dirs = fs.readdirSync(wingetDir).filter(d => d.startsWith('Gyan.FFmpeg'));
    for (const d of dirs) {
      const binDir = path.join(wingetDir, d);
      // Find the nested bin/ffmpeg.exe
      const subdirs = fs.readdirSync(binDir).filter(sd =>
        fs.statSync(path.join(binDir, sd)).isDirectory()
      );
      for (const sd of subdirs) {
        const candidate = path.join(binDir, sd, 'bin', 'ffmpeg.exe');
        if (fs.existsSync(candidate)) return candidate;
      }
    }
  }

  // 3. Last resort — hope it's somewhere on PATH
  return 'ffmpeg';
}

const FFMPEG_PATH = resolveFFmpegPath();
console.log(`[Stream] Using FFmpeg at: ${FFMPEG_PATH}`);

/**
 * Ensure the output directory exists for a camera.
 * Cleans any stale HLS files so a fresh FFmpeg session starts clean.
 */
function ensureCameraDir(cameraId) {
  const dir = path.join(STREAMS_DIR, cameraId);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  } else {
    // Remove stale .m3u8 and .ts files from a previous session
    try {
      const files = fs.readdirSync(dir);
      for (const f of files) {
        if (f.endsWith('.m3u8') || f.endsWith('.ts') || f.endsWith('.m4s') || f.endsWith('.tmp')) {
          fs.unlinkSync(path.join(dir, f));
        }
      }
      console.log(`[Stream] Cleaned ${files.length} stale files for camera ${cameraId}`);
    } catch (e) {
      console.warn(`[Stream] Could not clean dir for ${cameraId}:`, e.message);
    }
  }
  return dir;
}

/**
 * Start FFmpeg process to convert RTSP → HLS for a given camera
 * If rtspUrl starts with 'webcam:' it captures from a local DirectShow device instead.
 */
function startFFmpeg(cameraId, rtspUrl) {
  // Don't start if already running
  if (activeStreams.has(cameraId)) {
    const existing = activeStreams.get(cameraId);
    if (existing.process && !existing.process.killed) {
      console.log(`[Stream] FFmpeg already running for camera ${cameraId}`);
      return;
    }
  }

  const outputDir = ensureCameraDir(cameraId);
  const playlistPath = path.join(outputDir, 'index.m3u8');

  const isWebcam = rtspUrl.startsWith('webcam:');
  let ffmpegArgs;

  if (isWebcam) {
    // Local webcam capture via DirectShow (Windows)
    const deviceName = rtspUrl.replace('webcam:', '').trim() || 'Integrated Camera';
    console.log(`[Stream] Starting webcam capture for camera ${cameraId}: ${deviceName}`);
    ffmpegArgs = [
      '-f', 'dshow',                     // DirectShow input (Windows)
      '-thread_queue_size', '512',        // Prevent frame-drop on slow encode
      '-i', `video=${deviceName}`,        // Webcam device name
      '-c:v', 'libx264',
      '-preset', 'ultrafast',
      '-tune', 'zerolatency',
      '-g', '4',                          // Keyframe every 4 frames (~0.27s) – smaller GOP = lower latency
      '-keyint_min', '4',
      '-sc_threshold', '0',
      '-r', '15',                         // 15 fps
      '-s', '640x480',
      '-b:v', '800k',
      '-an',
      '-f', 'hls',
      '-hls_time', '0.5',                 // 0.5-second segments
      '-hls_list_size', '2',              // Keep only 2 segments in playlist (1 s of trail)
      '-hls_flags', 'delete_segments+independent_segments+split_by_time',
      '-flush_packets', '1',              // Flush output immediately after each packet
      '-hls_segment_filename', path.join(outputDir, 'seg_%03d.ts'),
      playlistPath,
      // Output 2: MJPEG → stdout (low-latency live display ~150-300ms)
      '-map', '0:v',
      '-r', '15',
      '-s', '640x480',
      '-c:v', 'mjpeg',
      '-q:v', '3',
      '-an',
      '-f', 'mjpeg',
      'pipe:1',
    ];
  } else {
    // RTSP stream
    let decodedUrl;
    try {
      decodedUrl = decodeURIComponent(rtspUrl);
    } catch (_) {
      decodedUrl = rtspUrl;
    }
    console.log(`[Stream] Starting FFmpeg for camera ${cameraId}`);
    ffmpegArgs = [
      '-fflags', 'nobuffer+discardcorrupt', // No input buffer, skip corrupt frames
      '-flags', 'low_delay',              // Low-delay decoding
      '-rtsp_transport', 'tcp',
      '-timeout', '10000000',
      '-probesize', '32',                 // Minimal probe size → start faster
      '-analyzeduration', '0',            // Skip long format analysis
      '-i', decodedUrl,
      '-c:v', 'libx264',
      '-preset', 'ultrafast',
      '-tune', 'zerolatency',
      '-g', '4',                          // Keyframe every 4 frames (~0.27s at 15fps)
      '-keyint_min', '4',
      '-sc_threshold', '0',
      '-r', '15',                         // 15 fps
      '-s', '640x480',
      '-b:v', '800k',
      '-an',
      '-f', 'hls',
      '-hls_time', '0.5',                 // 0.5-second segments
      '-hls_list_size', '2',              // Keep only 2 segments in playlist (1 s of trail)
      '-hls_flags', 'delete_segments+independent_segments+split_by_time',
      '-flush_packets', '1',              // Flush every packet immediately
      '-hls_segment_filename', path.join(outputDir, 'seg_%03d.ts'),
      playlistPath,
      // Output 2: MJPEG → stdout (low-latency live display ~150-300ms)
      '-map', '0:v',
      '-r', '15',
      '-s', '640x480',
      '-c:v', 'mjpeg',
      '-q:v', '3',
      '-an',
      '-f', 'mjpeg',
      'pipe:1',
    ];
  }

  const ffmpeg = spawn(FFMPEG_PATH, ffmpegArgs, {
    stdio: ['ignore', 'pipe', 'pipe'],
  });

  let hasOutput = false;

  ffmpeg.stderr.on('data', (data) => {
    const msg = data.toString();
    // Skip normal progress lines (FFmpeg writes all output to stderr)
    if (/^\s*frame=/.test(msg) || msg.includes('for writing')) return;

    // Log genuine errors only
    if (msg.match(/error|failed|refused|timeout|unauthorized|denied|no route/i)) {
      console.error(`[Stream][${cameraId}] FFmpeg error:`, msg.trim());
    }
    // Mark that we got output (stream is actually writing HLS segments)
    // Only match "Output #0" which means FFmpeg successfully opened output
    if (msg.includes('Output #0')) {
      hasOutput = true;
      console.log(`[Stream][${cameraId}] HLS output started`);
    }
  });

  // Auto-kill FFmpeg if it can't produce output within 20 seconds
  const startupTimeout = setTimeout(() => {
    if (!hasOutput && ffmpeg && !ffmpeg.killed) {
      console.error(`[Stream][${cameraId}] Timeout: no output after 20s - killing FFmpeg. Camera may be unreachable.`);
      ffmpeg.kill('SIGTERM');
    }
  }, 20000);

  ffmpeg.on('close', (code) => {
    clearTimeout(startupTimeout);
    console.log(`[Stream] FFmpeg for camera ${cameraId} exited with code ${code}`);
    const wasStreaming = hasOutput;
    activeStreams.delete(cameraId);

    // Auto-restart if FFmpeg was successfully streaming before it died
    // (code 0 = clean stop intentionally requested via stopFFmpeg, don't restart)
    if (wasStreaming && code !== 0 && code !== null) {
      console.log(`[Stream][${cameraId}] Unexpected exit — auto-restarting in 3 s…`);
      setTimeout(() => {
        // Only restart if nobody called stopFFmpeg in the meantime
        if (!activeStreams.has(cameraId)) {
          startFFmpeg(cameraId, rtspUrl);
        }
      }, 3000);
    }
  });

  ffmpeg.on('error', (err) => {
    clearTimeout(startupTimeout);
    console.error(`[Stream] Failed to start FFmpeg for ${cameraId}:`, err.message);
    const wasStreaming = hasOutput;
    activeStreams.delete(cameraId);

    if (wasStreaming) {
      console.log(`[Stream][${cameraId}] FFmpeg error — auto-restarting in 3 s…`);
      setTimeout(() => {
        if (!activeStreams.has(cameraId)) startFFmpeg(cameraId, rtspUrl);
      }, 3000);
    }
  });

  const entry = { process: ffmpeg, rtspUrl, clients: new Set() };
  activeStreams.set(cameraId, entry);

  // ── Parse MJPEG frames from stdout and push to all connected viewers ──
  const SOI = Buffer.from([0xFF, 0xD8]);
  const EOI = Buffer.from([0xFF, 0xD9]);
  let mjpegBuf = Buffer.alloc(0);

  ffmpeg.stdout.on('data', (chunk) => {
    mjpegBuf = Buffer.concat([mjpegBuf, chunk]);
    while (true) {
      const s = mjpegBuf.indexOf(SOI);
      if (s === -1) { mjpegBuf = Buffer.alloc(0); break; }
      const e = mjpegBuf.indexOf(EOI, s + 2);
      if (e === -1) { if (s > 0) mjpegBuf = mjpegBuf.slice(s); break; }
      const frame = mjpegBuf.slice(s, e + 2);
      mjpegBuf = mjpegBuf.slice(e + 2);
      const hdr = `--mjpegboundary\r\nContent-Type: image/jpeg\r\nContent-Length: ${frame.length}\r\n\r\n`;
      for (const clientRes of entry.clients) {
        try { clientRes.write(hdr); clientRes.write(frame); clientRes.write('\r\n'); }
        catch (_) { entry.clients.delete(clientRes); }
      }
    }
  });
}

/**
 * Stop FFmpeg process for a camera
 */
function stopFFmpeg(cameraId) {
  const entry = activeStreams.get(cameraId);
  if (entry) {
    if (entry.clients) {
      for (const res of entry.clients) { try { res.end(); } catch (_) {} }
    }
    if (entry.process) {
      console.log(`[Stream] Stopping FFmpeg for camera ${cameraId}`);
      entry.process.kill('SIGTERM');
    }
    activeStreams.delete(cameraId);
  }
}

/**
 * @desc    Start streaming for a camera (RTSP→HLS)
 * @route   POST /api/stream/start
 * @access  Private
 */
const startStream = async (req, res, next) => {
  try {
    const { cameraId, rtspUrl } = req.body;

    if (!cameraId || !rtspUrl) {
      return res.status(400).json({
        success: false,
        error: 'cameraId and rtspUrl are required',
      });
    }

    startFFmpeg(cameraId, rtspUrl);

    res.json({
      success: true,
      message: `Stream started for camera ${cameraId}`,
      hlsUrl: `/api/stream/hls/${cameraId}/index.m3u8`,
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Stop streaming for a camera
 * @route   POST /api/stream/stop
 * @access  Private
 */
const stopStream = async (req, res, next) => {
  try {
    const { cameraId } = req.body;

    if (!cameraId) {
      return res.status(400).json({
        success: false,
        error: 'cameraId is required',
      });
    }

    stopFFmpeg(cameraId);

    res.json({
      success: true,
      message: `Stream stopped for camera ${cameraId}`,
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Start all streams for the authenticated user
 * @route   POST /api/stream/start-all
 * @access  Private
 */
const startAllStreams = async (req, res, next) => {
  try {
    const user = await User.findById(req.user._id);
    if (!user) {
      return res.status(404).json({ success: false, error: 'User not found' });
    }

    const started = [];

    // Primary camera
    if (user.rtspUrl) {
      startFFmpeg('primary', user.rtspUrl);
      started.push({ cameraId: 'primary', rtspUrl: user.rtspUrl });
    }

    // Extra cameras
    if (user.cameras && user.cameras.length > 0) {
      user.cameras.forEach((cam, index) => {
        const camId = cam._id?.toString() || `extra-${index}`;
        if (cam.rtspUrl) {
          startFFmpeg(camId, cam.rtspUrl);
          started.push({ cameraId: camId, rtspUrl: cam.rtspUrl });
        }
      });
    }

    res.json({
      success: true,
      message: `Started ${started.length} stream(s)`,
      streams: started.map((s) => ({
        cameraId: s.cameraId,
        hlsUrl: `/api/stream/hls/${s.cameraId}/index.m3u8`,
      })),
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Get status of active streams
 * @route   GET /api/stream/status
 * @access  Private
 */
const getStreamStatus = async (req, res) => {
  const streams = [];
  for (const [cameraId, entry] of activeStreams) {
    streams.push({
      cameraId,
      active: entry.process && !entry.process.killed,
    });
  }
  res.json({ success: true, streams });
};

/**
 * @desc    Stop all active streams
 * @route   POST /api/stream/stop-all
 * @access  Private
 */
const stopAllStreams = async (req, res, next) => {
  try {
    const stopped = [];
    for (const [cameraId] of activeStreams) {
      stopFFmpeg(cameraId);
      stopped.push(cameraId);
    }
    res.json({
      success: true,
      message: `Stopped ${stopped.length} stream(s)`,
      stopped,
    });
  } catch (error) {
    next(error);
  }
};

/**
 * Cleanup all streams on server shutdown
 */
const cleanupAllStreams = () => {
  for (const [cameraId] of activeStreams) {
    stopFFmpeg(cameraId);
  }
};

/**
 * @desc    Start webcam as test stream
 * @route   POST /api/stream/webcam
 * @access  Private
 */
const startWebcam = async (req, res, next) => {
  try {
    const { cameraId, deviceName } = req.body;
    const camId = cameraId || 'webcam-test';
    const device = deviceName || 'Integrated Camera';

    startFFmpeg(camId, `webcam:${device}`);

    res.json({
      success: true,
      message: `Webcam stream started (${device})`,
      cameraId: camId,
      hlsUrl: `/api/stream/hls/${camId}/index.m3u8`,
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc  Stream MJPEG from a running camera to an HTTP multipart client
 * @route GET /api/stream/mjpeg/:cameraId
 */
const mjpegStream = (req, res) => {
  const { cameraId } = req.params;
  const entry = activeStreams.get(cameraId);
  if (!entry || !entry.process || entry.process.killed) {
    return res.status(503).json({ success: false, error: `No active stream for ${cameraId}` });
  }
  res.writeHead(200, {
    'Content-Type': 'multipart/x-mixed-replace; boundary=mjpegboundary',
    'Cache-Control': 'no-cache, no-store, must-revalidate',
    'Pragma': 'no-cache',
    'Connection': 'keep-alive',
    'Access-Control-Allow-Origin': '*',
  });
  entry.clients.add(res);
  req.on('close', () => entry.clients.delete(res));
};

module.exports = {
  startStream,
  stopStream,
  startAllStreams,
  stopAllStreams,
  getStreamStatus,
  cleanupAllStreams,
  startWebcam,
  startFFmpeg,
  stopFFmpeg,
  mjpegStream,
};
