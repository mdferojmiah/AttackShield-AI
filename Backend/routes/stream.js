/**
 * Stream Routes
 * Manage RTSP → HLS streaming via FFmpeg
 */

const express = require('express');
const router = express.Router();
const path = require('path');

const {
  startStream,
  stopStream,
  startAllStreams,
  stopAllStreams,
  getStreamStatus,
  startWebcam,
  mjpegStream,
} = require('../controllers/streamController');
const { authenticate } = require('../middleware/auth');

// Serve HLS files (.m3u8, .m4s, .mp4, .ts) from public/streams/<cameraId>/
router.use(
  '/hls',
  express.static(path.join(__dirname, '..', 'public', 'streams'), {
    setHeaders(res, filePath) {
      // CORS + no-cache for live stream
      res.setHeader('Access-Control-Allow-Origin', '*');
      res.setHeader('Cache-Control', 'no-cache, no-store, must-revalidate');

      if (filePath.endsWith('.m3u8')) {
        res.setHeader('Content-Type', 'application/vnd.apple.mpegurl');
      } else if (filePath.endsWith('.m4s')) {
        res.setHeader('Content-Type', 'video/iso.segment');
      } else if (filePath.endsWith('.mp4')) {
        res.setHeader('Content-Type', 'video/mp4');
      } else if (filePath.endsWith('.ts')) {
        res.setHeader('Content-Type', 'video/mp2t');
      }
    },
  })
);

// Protected routes
router.post('/start', authenticate, startStream);
router.post('/stop', authenticate, stopStream);
router.post('/start-all', authenticate, startAllStreams);
router.post('/stop-all', authenticate, stopAllStreams);
router.post('/webcam', authenticate, startWebcam);
router.get('/status', authenticate, getStreamStatus);
// MJPEG live viewer (no auth: <img> src cannot carry Authorization headers)
router.get('/mjpeg/:cameraId', mjpegStream);

module.exports = router;
