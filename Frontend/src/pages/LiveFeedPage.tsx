/**
 * Live Feed Page
 *
 * Streams are started when this page mounts and stopped when it unmounts.
 * MJPEG over HTTP multipart for ultra-low latency (~150–300 ms) display.
 *
 * Architecture:
 *   RTSP → FFmpeg → MJPEG pipe → Node.js → multipart/x-mixed-replace → <img> in browser
 *   (HLS is also produced in parallel for the AI service)
 */

import React, { useState, useEffect, useRef, useCallback } from 'react';
import {
  HiArrowPath,
  HiExclamationTriangle,
  HiVideoCamera,
  HiVideoCameraSlash,
  HiPlusCircle,
  HiXMark,
  HiMapPin,
  HiWifi,
  HiSignal,
  HiLockClosed,
  HiUser,
} from 'react-icons/hi2';
import { CamerasAPI, StreamAPI } from '@/services/api';
import { useCameras, useSocket } from '@/context';
import type { CameraData } from '@/context';
import { useDocumentTitle } from '@/hooks';
import { LoadingSpinner } from '@/components';
import { API_CONFIG } from '@/config';
import toast from 'react-hot-toast';

export default function LiveFeedPage() {
  useDocumentTitle('Live Feed');

  const { cameras, setCameras, userName, loading, loaded, loadCameras } =
    useCameras();

  const [isPlaying, setIsPlaying] = useState(false);
  const [detectionAlert, setDetectionAlert] = useState<{
    weaponType: string;
    confidence: number;
    cameraName?: string;
  } | null>(null);
  const { socket, sendDetectionRequest } = useSocket();

  // Add camera form
  const [showAddForm, setShowAddForm] = useState(false);
  const [addLoading, setAddLoading] = useState(false);
  const [webcamLoading, setWebcamLoading] = useState(false);
  const [newCamera, setNewCamera] = useState({
    name: '',
    cameraIp: '',
    cameraUsername: '',
    cameraPassword: '',
    cameraPort: '',
    cameraBrand: '',
    location: '',
  });

  // Load cameras if not loaded yet
  useEffect(() => {
    if (!loaded) loadCameras();
  }, [loaded, loadCameras]);

  // Reset LIVE status when all cameras are removed
  useEffect(() => {
    if (cameras.length === 0) setIsPlaying(false);
  }, [cameras.length]);

  // ── Start streams on mount, stop ALL on unmount ──────────────────
  useEffect(() => {
    if (!loaded || cameras.length === 0) return;

    const startStreams = async () => {
      // Start DB-backed streams (primary + extra cameras)
      await StreamAPI.startAll().catch((err) =>
        console.warn('Could not start streams:', err),
      );

      // Also restart any local webcam cameras (not stored in DB)
      for (const cam of cameras) {
        if (cam.id?.startsWith('webcam')) {
          const deviceName =
            cam.stream_url?.replace('webcam:', '') || 'Integrated Camera';
          await StreamAPI.startWebcam(cam.id, deviceName).catch(() => {});
        }
      }
    };

    startStreams();

    // Stop all streams when we leave this page
    return () => {
      StreamAPI.stopAll().catch((err) =>
        console.warn('Could not stop streams:', err),
      );
    };
  }, [loaded, cameras.length]); // only re-run when camera count changes

  // Socket listener for weapon-detected
  useEffect(() => {
    if (!socket) return;
    const handler = (data: {
      weaponType: string;
      confidence: number;
      cameraName?: string;
    }) => {
      setDetectionAlert(data);
      toast.error(
        `Weapon Detected: ${data.weaponType} (${(data.confidence * 100).toFixed(1)}%)`,
        { duration: 8000 },
      );
      setTimeout(() => setDetectionAlert(null), 10000);
    };
    socket.on('weapon-detected', handler);
    return () => {
      socket.off('weapon-detected', handler);
    };
  }, [socket]);

  // Send detection requests when streams are ready
  useEffect(() => {
    if (isPlaying && cameras.length > 0 && userName && sendDetectionRequest) {
      cameras.forEach((cam) => {
        // For webcam cameras, pass the HLS URL so the AI service reads
        // from FFmpeg's output instead of trying to open the device directly
        let streamUrl = cam.stream_url;
        if (cam.stream_url?.startsWith('webcam:')) {
          streamUrl = `${API_CONFIG.BASE_URL}/streams/${cam.id}/index.m3u8`;
        }
        sendDetectionRequest({
          stream_url: streamUrl,
          user: userName,
          location: cam.location,
          camera_name: cam.camera_name,
          camera_id: cam.id,
        });
      });
    }
  }, [isPlaying, cameras, userName, sendDetectionRequest]);

  // Test with PC Webcam
  const handleTestWebcam = async () => {
    setWebcamLoading(true);
    try {
      const result = await StreamAPI.startWebcam(
        'webcam-test',
        'Integrated Camera',
      );
      if (result.success) {
        toast.success('Webcam stream started! Adding to feed...');
        setCameras((prev) => {
          if (prev.some((c) => c.id === 'webcam-test')) return prev;
          return [
            ...prev,
            {
              id: 'webcam-test',
              camera_name: 'PC Webcam (Test)',
              stream_url: 'webcam:Integrated Camera',
              location: 'Local Machine',
            },
          ];
        });
      } else {
        toast.error('Failed to start webcam');
      }
    } catch {
      toast.error('Network error');
    } finally {
      setWebcamLoading(false);
    }
  };

  // Remove a camera — stop FFmpeg, remove from state, and from DB if applicable
  const handleRemoveCamera = async (cameraId: string, cameraName: string) => {
    if (
      !window.confirm(`Remove camera "${cameraName}"? This cannot be undone.`)
    )
      return;

    // 1. Always stop the FFmpeg stream
    StreamAPI.stop(cameraId).catch(() => {});

    // 2. Remove from local state immediately
    setCameras((prev) => prev.filter((c) => c.id !== cameraId));

    // 3. For DB-stored cameras (not local webcam/primary), also remove from backend
    const isLocal = cameraId === 'primary' || cameraId.startsWith('webcam');
    if (!isLocal) {
      try {
        await CamerasAPI.remove(cameraId);
      } catch {
        // Already removed from UI — ignore backend errors
      }
    }

    toast.success('Camera removed');
  };

  // Add a new camera
  const handleAddCamera = async () => {
    if (!newCamera.name || !newCamera.cameraIp || !newCamera.location) {
      toast.error('Please fill in required fields (Name, IP, Location)');
      return;
    }
    setAddLoading(true);
    try {
      const result = await CamerasAPI.add({
        name: newCamera.name,
        location: newCamera.location,
        cameraIp: newCamera.cameraIp,
        cameraUsername: newCamera.cameraUsername,
        cameraPassword: newCamera.cameraPassword,
        cameraPort: newCamera.cameraPort || undefined,
        cameraBrand: newCamera.cameraBrand || undefined,
      });
      if (result.success) {
        toast.success('Camera added!');
        setNewCamera({
          name: '',
          cameraIp: '',
          cameraUsername: '',
          cameraPassword: '',
          cameraPort: '',
          cameraBrand: '',
          location: '',
        });
        setShowAddForm(false);
        loadCameras();
      } else {
        toast.error('Failed to add camera');
      }
    } catch {
      toast.error('Network error');
    } finally {
      setAddLoading(false);
    }
  };

  if (loading && !loaded)
    return <LoadingSpinner text="Initializing Streams..." />;

  return (
    <div className="p-4 lg:p-8 max-w-6xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-bold text-slate-800 dark:text-white">
            Live Cameras
          </h2>
          <p className="text-sm text-slate-400">{userName}</p>
        </div>
        <button
          onClick={loadCameras}
          className="p-2 text-primary hover:bg-primary/10 rounded-lg transition-colors"
          title="Refresh"
        >
          <HiArrowPath size={24} />
        </button>
      </div>

      {/* Detection Alert Banner */}
      {detectionAlert && (
        <div className="flex items-center gap-3 bg-red-500 text-white rounded-xl px-5 py-4 animate-pulse">
          <HiExclamationTriangle size={24} />
          <span className="font-bold">
            Weapon Detected: {detectionAlert.weaponType} (
            {(detectionAlert.confidence * 100).toFixed(1)}%)
            {detectionAlert.cameraName
              ? ` - ${detectionAlert.cameraName}`
              : ''}
          </span>
        </div>
      )}

      {/* Camera Grid */}
      {cameras.length === 0 ? (
        <div className="text-center py-20">
          <HiVideoCameraSlash className="mx-auto text-slate-500" size={56} />
          <p className="text-slate-400 mt-4 text-lg">No cameras configured</p>
          <p className="text-slate-500 text-sm mt-1">
            Add a camera to start monitoring
          </p>
        </div>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {cameras.map((cam) => (
            <CameraCard
              key={cam.id}
              camera={cam}
              onPlaying={() => setIsPlaying(true)}
              onRemove={() => handleRemoveCamera(cam.id, cam.camera_name)}
            />
          ))}
        </div>
      )}

      {/* Status Badge */}
      <div className="flex items-center gap-2">
        <span
          className={`w-2.5 h-2.5 rounded-full ${isPlaying && cameras.length > 0 ? 'bg-emerald-400' : 'bg-red-400'}`}
        />
        <span className="text-xs font-bold text-slate-300 uppercase tracking-wider">
          {isPlaying && cameras.length > 0 ? 'LIVE' : 'OFFLINE'}
        </span>
      </div>

      {/* Add Camera Button + Test Webcam */}
      {!showAddForm && (
        <div className="flex items-center justify-center gap-3 flex-wrap">
          <button
            onClick={() => setShowAddForm(true)}
            className="btn-primary flex items-center gap-2"
          >
            <HiPlusCircle size={20} />
            Add More Camera
          </button>
          <button
            onClick={handleTestWebcam}
            disabled={webcamLoading}
            className="flex items-center gap-2 px-4 py-2 bg-emerald-600 hover:bg-emerald-700 text-white rounded-lg font-medium transition-colors disabled:opacity-50"
          >
            <HiVideoCamera size={20} />
            {webcamLoading ? 'Starting...' : 'Test with Webcam'}
          </button>
        </div>
      )}

      {/* Add Camera Form */}
      {showAddForm && (
        <div className="card max-w-lg mx-auto space-y-3">
          <div className="flex items-center justify-between mb-2">
            <h3 className="text-lg font-bold text-slate-800 dark:text-white">
              Add Camera
            </h3>
            <button
              onClick={() => setShowAddForm(false)}
              className="text-slate-400 hover:text-slate-800 dark:hover:text-white"
            >
              <HiXMark size={22} />
            </button>
          </div>

          {[
            {
              icon: <HiVideoCamera size={16} />,
              ph: 'Camera Name *',
              field: 'name',
            },
            {
              icon: <HiSignal size={16} />,
              ph: 'Camera IP *',
              field: 'cameraIp',
            },
            {
              icon: <HiUser size={16} />,
              ph: 'Username (optional)',
              field: 'cameraUsername',
            },
            {
              icon: <HiLockClosed size={16} />,
              ph: 'Password (optional)',
              field: 'cameraPassword',
              type: 'password',
            },
            {
              icon: <HiWifi size={16} />,
              ph: 'Port (default 554)',
              field: 'cameraPort',
            },
            {
              icon: <HiVideoCamera size={16} />,
              ph: 'Brand (optional)',
              field: 'cameraBrand',
            },
            {
              icon: <HiMapPin size={16} />,
              ph: 'Location *',
              field: 'location',
            },
          ].map(({ icon, ph, field, type }) => (
            <div key={field} className="relative">
              <span className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400">
                {icon}
              </span>
              <input
                type={type || 'text'}
                placeholder={ph}
                className="input-field pl-9 py-2 text-sm"
                value={(newCamera as Record<string, string>)[field]}
                onChange={(e) =>
                  setNewCamera((prev) => ({ ...prev, [field]: e.target.value }))
                }
              />
            </div>
          ))}

          <button
            onClick={handleAddCamera}
            disabled={addLoading}
            className="btn-accent w-full"
          >
            {addLoading ? 'Saving...' : 'Save Camera'}
          </button>
        </div>
      )}
    </div>
  );
}

// ─── Alarm / Warning Sound (Web Audio API, no audio files needed) ─
function playAlarmSound(type: 'weapon' | 'suspicious') {
  try {
    const AudioCtx = window.AudioContext || (window as any).webkitAudioContext;
    if (!AudioCtx) return;
    const ctx = new AudioCtx();

    if (type === 'weapon') {
      // 3 rapid high-pitched beeps (urgent alarm)
      [0, 0.18, 0.36].forEach((offset) => {
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.type = 'square';
        osc.frequency.value = 940;
        gain.gain.setValueAtTime(0.35, ctx.currentTime + offset);
        gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + offset + 0.16);
        osc.start(ctx.currentTime + offset);
        osc.stop(ctx.currentTime + offset + 0.16);
      });
    } else {
      // 2 slower medium-pitched warning tones
      [0, 0.45].forEach((offset) => {
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.type = 'triangle';
        osc.frequency.value = 520;
        gain.gain.setValueAtTime(0.2, ctx.currentTime + offset);
        gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + offset + 0.25);
        osc.start(ctx.currentTime + offset);
        osc.stop(ctx.currentTime + offset + 0.25);
      });
    }
  } catch (_) { /* AudioContext unavailable */ }
}

// ─── Camera Card with HLS Player + Stall Recovery ────────────────
interface CameraCardProps {
  camera: CameraData;
  onPlaying: () => void;
  onRemove: () => void;
}

function CameraCard({ camera, onPlaying, onRemove }: CameraCardProps) {
  const imgRef = useRef<HTMLImageElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  // Map of face boxes: position key → {bbox, label, confidence, expires}
  const activeFacesRef = useRef<Map<string, {
    bbox: { x: number; y: number; w: number; h: number };
    label: string; confidence: number; expires: number;
  }>>(new Map());
  // Latest alert overlay (weapon / suspicious) — clears after 3 s
  const activeAlertRef = useRef<{
    type: string; label: string; confidence: number;
    bbox: { x: number; y: number; w: number; h: number } | null;
    expires: number;
  } | null>(null);
  const rafRef = useRef<number>(0);
  const onPlayingRef = useRef(onPlaying);
  const [error, setError] = useState<string | null>(null);
  const [buffering, setBuffering] = useState(true);
  const mjpegReconnectTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const { socket } = useSocket();

  useEffect(() => {
    onPlayingRef.current = onPlaying;
  }, [onPlaying]);

  // ── requestAnimationFrame render loop ─────────────────────────
  // Redraws all active overlays on every frame:
  //   • Face boxes (green) — persistent, TTL refreshed every ~3 s by AI
  //   • Weapon / suspicious boxes — shown for 3 s then expire
  const renderLoop = useCallback(() => {
    const canvas = canvasRef.current;
    const img    = imgRef.current;
    if (!canvas) { rafRef.current = requestAnimationFrame(renderLoop); return; }

    const cw = canvas.clientWidth  || 640;
    const ch = canvas.clientHeight || 360;
    if (canvas.width !== cw)  canvas.width  = cw;
    if (canvas.height !== ch) canvas.height = ch;

    const ctx = canvas.getContext('2d');
    if (!ctx) { rafRef.current = requestAnimationFrame(renderLoop); return; }

    ctx.clearRect(0, 0, cw, ch);
    const now = Date.now();

    // ── Compute object-cover coordinate transform ──────────────────
    // The MJPEG frame (e.g. 640×480, 4:3) is object-cover displayed in a
    // 16:9 container; the image is zoomed to fill the box and centred.
    // Bbox coordinates are normalised to [0,1] relative to the raw frame.
    //
    // If naturalWidth/Height are not yet available fall back to canvas size so
    // boxes always appear (slight squash acceptable vs nothing visible).
    const hasNat = img && img.naturalWidth > 0 && img.naturalHeight > 0;
    const natW   = hasNat ? img!.naturalWidth  : 640;   // assume 640×480 stream
    const natH   = hasNat ? img!.naturalHeight : 480;
    const covScale = Math.max(cw / natW, ch / natH);    // object-cover scale
    const rW    = natW * covScale;
    const rH    = natH * covScale;
    const offX  = (cw - rW) / 2;
    const offY  = (ch - rH) / 2;

    // Convert normalised bbox → canvas pixel rect.
    // Label y is clamped so the text tag is always on-screen even when the
    // face/weapon box starts above the top edge of the canvas.
    const LABEL_H = 22;
    const toPx = (bx: number, by: number, bw: number, bh: number) => {
      const px = bx * rW + offX;
      const py = by * rH + offY;
      const pw = bw * rW;
      const ph = bh * rH;
      // clamp label so it never goes above canvas top
      const labelY = Math.max(py, LABEL_H);
      return { px, py, pw, ph, labelY };
    };

    // ── Draw persistent face boxes (green) ────────────────────────
    for (const [key, f] of activeFacesRef.current) {
      if (now > f.expires) { activeFacesRef.current.delete(key); continue; }
      const { px, py, pw, ph, labelY } = toPx(f.bbox.x, f.bbox.y, f.bbox.w, f.bbox.h);
      ctx.strokeStyle = '#22c55e';
      ctx.lineWidth   = 2.5;
      ctx.strokeRect(px, py, pw, ph);
      // Corner markers for a cleaner look
      const corner = Math.min(pw, ph) * 0.18;
      ctx.lineWidth = 3;
      [[px, py], [px + pw, py], [px, py + ph], [px + pw, py + ph]].forEach(([cx, cy], i) => {
        ctx.beginPath();
        ctx.moveTo(cx + (i % 2 === 0 ? corner : -corner), cy);
        ctx.lineTo(cx, cy);
        ctx.lineTo(cx, cy + (i < 2 ? corner : -corner));
        ctx.stroke();
      });
      ctx.lineWidth = 2.5;
      const lbl = `${f.label}  ${(f.confidence * 100).toFixed(0)}%`;
      ctx.font = 'bold 12px Inter, sans-serif';
      const tw = ctx.measureText(lbl).width + 10;
      ctx.fillStyle = '#22c55e'; ctx.fillRect(px, labelY - LABEL_H, tw, LABEL_H);
      ctx.fillStyle = '#fff';    ctx.fillText(lbl, px + 5, labelY - 6);
    }

    // ── Draw alert overlay (weapon=red, suspicious=yellow) ────────
    const alert = activeAlertRef.current;
    if (alert) {
      if (now > alert.expires) {
        activeAlertRef.current = null;
      } else {
        const color = alert.type === 'weapon' ? '#ef4444' : '#eab308';
        const lbl   = `${alert.label}  ${(alert.confidence * 100).toFixed(0)}%`;
        if (alert.bbox) {
          const { px, py, pw, ph, labelY } = toPx(alert.bbox.x, alert.bbox.y, alert.bbox.w, alert.bbox.h);
          ctx.strokeStyle = color; ctx.lineWidth = 3;
          ctx.strokeRect(px, py, pw, ph);
          // Animated pulsing highlight
          ctx.fillStyle = color + '22';
          ctx.fillRect(px, py, pw, ph);
          ctx.font = 'bold 13px Inter, sans-serif';
          const tw = ctx.measureText(lbl).width + 12;
          ctx.fillStyle = color; ctx.fillRect(px, labelY - 24, tw, 24);
          ctx.fillStyle = '#fff'; ctx.fillText(lbl, px + 6, labelY - 7);
        } else {
          ctx.strokeStyle = color; ctx.lineWidth = 4;
          ctx.strokeRect(3, 3, cw - 6, ch - 6);
          ctx.fillStyle = color + 'cc'; ctx.fillRect(0, 0, cw, 30);
          ctx.fillStyle = '#fff'; ctx.font = 'bold 13px Inter, sans-serif';
          ctx.fillText(lbl, 10, 20);
        }
      }
    }

    rafRef.current = requestAnimationFrame(renderLoop);
  }, []);

  // Start render loop on mount, cancel on unmount
  useEffect(() => {
    rafRef.current = requestAnimationFrame(renderLoop);
    return () => cancelAnimationFrame(rafRef.current);
  }, [renderLoop]);

  // ── Socket listener: detection-overlay ────────────────────
  useEffect(() => {
    if (!socket) return;
    const handler = (data: {
      cameraId: string; type: string; label: string;
      confidence: number; bbox?: any; sound?: string;
    }) => {
      if (data.cameraId !== camera.id) return;

      if (data.type === 'face' && data.bbox) {
        // Key each person by their stable AI label ("Person 1", "Person 2", …)
        // so that the same person's box always refreshes the same canvas slot,
        // while different people get independent slots.
        const key = data.label || `${(data.bbox.x * 10).toFixed(0)}:${(data.bbox.y * 10).toFixed(0)}`;
        activeFacesRef.current.set(key, {
          bbox: data.bbox, label: data.label,
          confidence: data.confidence, expires: Date.now() + 8000,
        });
      } else {
        activeAlertRef.current = {
          type: data.type, label: data.label,
          confidence: data.confidence,
          bbox: data.bbox ?? null,
          expires: Date.now() + 3000,
        };
        if (data.sound === 'weapon')     playAlarmSound('weapon');
        if (data.sound === 'suspicious') playAlarmSound('suspicious');
      }
    };
    socket.on('detection-overlay', handler);
    return () => { socket.off('detection-overlay', handler); };
  }, [socket, camera.id]);

  // Use /streams/ path (bypasses /api/ rate limiter)
  const hlsUrl   = `${API_CONFIG.BASE_URL}/streams/${camera.id}/index.m3u8`;
  const mjpegUrl = `${API_CONFIG.BASE_URL}/api/stream/mjpeg/${camera.id}`;

  // ── MJPEG reconnect on error ──────────────────────────────────────
  // Called when the browser's <img> HTTP connection to the MJPEG endpoint
  // drops (FFmpeg restarted, network blip, etc.). Schedules a fresh
  // connection with an incremented cache-bust parameter so the browser
  // doesn't serve a stale response from its cache.
  const mjpegRetryCount = useRef(0);
  const handleMjpegError = useCallback(() => {
    if (!imgRef.current || buffering) return;
    if (mjpegReconnectTimer.current) clearTimeout(mjpegReconnectTimer.current);
    const delay = Math.min(1000 * Math.pow(1.5, mjpegRetryCount.current), 10000);
    console.warn(`[Camera ${camera.id}] MJPEG error — reconnect in ${(delay / 1000).toFixed(1)} s (attempt ${mjpegRetryCount.current + 1})`);
    mjpegReconnectTimer.current = setTimeout(() => {
      if (imgRef.current) {
        mjpegRetryCount.current += 1;
        imgRef.current.src = `${mjpegUrl}?t=${Date.now()}`;
      }
    }, delay);
  }, [mjpegUrl, camera.id, buffering]);

  // Reset retry counter when stream loads successfully
  const handleMjpegLoad = useCallback(() => {
    mjpegRetryCount.current = 0;
  }, []);

  // ── MJPEG player: poll until HLS playlist is ready (signals FFmpeg is up),
  //    then point <img> at the MJPEG endpoint for ~150-300ms live display ──
  useEffect(() => {
    let destroyed = false;
    let attempt = 0;
    const maxAttempts = 30; // 30 × 500 ms = 15 s max
    const pollTimer = setInterval(async () => {
      if (destroyed) { clearInterval(pollTimer); return; }
      attempt++;
      try {
        const res = await fetch(hlsUrl, { method: 'HEAD' });
        if (res.ok) {
          clearInterval(pollTimer);
          if (imgRef.current) imgRef.current.src = mjpegUrl;
          setBuffering(false);
          onPlayingRef.current();
        } else if (attempt >= maxAttempts) {
          clearInterval(pollTimer);
          setError('Stream took too long to start. Try refreshing.');
          setBuffering(false);
        }
      } catch {
        if (attempt >= maxAttempts) {
          clearInterval(pollTimer);
          setError('Stream took too long to start. Try refreshing.');
          setBuffering(false);
        }
      }
    }, 500);

    return () => {
      destroyed = true;
      clearInterval(pollTimer);
      if (imgRef.current) imgRef.current.src = '';
      if (mjpegReconnectTimer.current) clearTimeout(mjpegReconnectTimer.current);
    };
  }, [hlsUrl, mjpegUrl, camera.id]);

  const retry = useCallback(async () => {
    setError(null);
    setBuffering(true);
    if (imgRef.current) imgRef.current.src = '';
    try { await StreamAPI.start(camera.id, camera.stream_url); } catch { /* ignore */ }
    let attempt = 0;
    const timer = setInterval(async () => {
      attempt++;
      try {
        const res = await fetch(hlsUrl, { method: 'HEAD' });
        if (res.ok) {
          clearInterval(timer);
          if (imgRef.current) imgRef.current.src = mjpegUrl;
          setBuffering(false);
          onPlayingRef.current();
        }
      } catch { /* keep polling */ }
      if (attempt >= 30) {
        clearInterval(timer);
        setError('Stream unavailable. The camera may be offline.');
        setBuffering(false);
      }
    }, 500);
  }, [hlsUrl, mjpegUrl, camera.id, camera.stream_url]);

  return (
    <div className="card overflow-hidden">
      <div className="flex items-start justify-between px-1 pb-2">
        <div className="min-w-0">
          <h4 className="text-sm font-semibold text-slate-800 dark:text-white truncate">
            {camera.camera_name}
          </h4>
          <p className="text-xs text-slate-400 truncate flex items-center gap-1">
            <HiMapPin size={12} /> {camera.location}
          </p>
        </div>
        <button
          onClick={onRemove}
          className="p-1 text-slate-400 hover:text-red-400 hover:bg-red-400/10 rounded-lg transition-colors flex-shrink-0"
          title="Remove Camera"
        >
          <HiXMark size={18} />
        </button>
      </div>

      <div className="relative w-full aspect-video bg-black rounded-xl overflow-hidden">
        <img
          ref={imgRef}
          className="w-full h-full object-cover"
          alt="Camera Feed"
          onError={handleMjpegError}
          onLoad={handleMjpegLoad}
        />
        {/* Detection overlay canvas — colored bbox drawn by socket events */}
        <canvas
          ref={canvasRef}
          className="absolute inset-0 w-full h-full pointer-events-none"
          style={{ zIndex: 10 }}
        />

        {/* Buffering / Error Overlay */}
        {(buffering || error) && (
          <div className="absolute inset-0 bg-dark-bg/90 flex items-center justify-center">
            {error ? (
              <div className="text-center space-y-3">
                <HiVideoCameraSlash
                  className="mx-auto text-red-400"
                  size={40}
                />
                <p className="text-sm text-red-400 max-w-xs">{error}</p>
                <button onClick={retry} className="btn-primary text-sm">
                  Retry Stream
                </button>
              </div>
            ) : (
              <div className="text-center space-y-2">
                <div className="w-8 h-8 border-4 border-slate-600 border-t-primary rounded-full animate-spin mx-auto" />
                <p className="text-sm text-primary">Connecting to Camera...</p>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
