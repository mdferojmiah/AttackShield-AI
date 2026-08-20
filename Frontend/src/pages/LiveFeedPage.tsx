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
import { UserStorage } from '@/services/storage';
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

  const [liveCameraIds, setLiveCameraIds] = useState<Set<string>>(new Set());
  const detectionRequestedRef = useRef<Set<string>>(new Set());
  const [detectionAlert, setDetectionAlert] = useState<{
    type?: 'weapon' | 'hit_list';
    weaponType: string;
    confidence: number;
    cameraName?: string;
  } | null>(null);
  const { socket, connectionEpoch, sendDetectionRequest, stopDetectionRequest } = useSocket();

  // Add camera form
  const [showAddForm, setShowAddForm] = useState(false);
  const [addLoading, setAddLoading] = useState(false);
  const [webcamLoading, setWebcamLoading] = useState(false);
  const [newCamera, setNewCamera] = useState({
    name: '',
    connectionMode: 'ip' as 'ip' | 'rtsp',
    rtspUrl: '',
    cameraIp: '',
    cameraUsername: '',
    cameraPassword: '',
    cameraPort: '',
    cameraBrand: '',
    cameraPath: '',
    location: '',
  });

  // Load cameras if not loaded yet
  useEffect(() => {
    if (!loaded) loadCameras();
  }, [loaded, loadCameras]);

  // Reset LIVE status when all cameras are removed
  useEffect(() => {
    if (cameras.length === 0) setLiveCameraIds(new Set());
  }, [cameras.length]);

  // ── Start streams on mount, stop ALL on unmount ──────────────────
  useEffect(() => {
    if (!loaded || cameras.length === 0) return;

    let cancelled = false;

    const startStreams = async () => {
      // Start DB-backed streams (primary + extra cameras)
      await StreamAPI.startAll().catch((err) =>
        console.warn('Could not start streams:', err),
      );

      if (cancelled) {
        await StreamAPI.stopAll().catch(() => {});
        return;
      }

      // Also restart any local webcam cameras (not stored in DB)
      for (const cam of cameras) {
        if (cancelled) break;
        if (cam.id?.startsWith('webcam')) {
          const deviceName =
            cam.stream_url?.replace('webcam:', '') || 'Integrated Camera';
          await StreamAPI.startWebcam(cam.id, deviceName).catch(() => {});
        }
      }

      if (cancelled) await StreamAPI.stopAll().catch(() => {});
    };

    startStreams();

    // Stop all streams when we leave this page
    return () => {
      cancelled = true;
      stopDetectionRequest();
      StreamAPI.stopAll().catch((err) =>
        console.warn('Could not stop streams:', err),
      );
    };
  }, [loaded, cameras.length, stopDetectionRequest]); // only re-run when camera count changes

  // Socket listener for weapon-detected
  useEffect(() => {
    if (!socket) return;
    const handler = (data: {
      type?: 'weapon' | 'hit_list';
      weaponType: string;
      confidence: number;
      cameraName?: string;
    }) => {
      setDetectionAlert(data);
      const label = data.type === 'hit_list' ? 'Hit List Match' : 'Weapon Detected';
      toast.error(
        `${label}: ${data.weaponType} (${(data.confidence * 100).toFixed(1)}%)`,
        { duration: 8000 },
      );
      setTimeout(() => setDetectionAlert(null), 10000);
    };
    socket.on('weapon-detected', handler);
    return () => {
      socket.off('weapon-detected', handler);
    };
  }, [socket]);

  // Send detection requests when streams and SignalR are both ready. The live
  // image can load before SignalR connects, so this effect must retry on socket
  // state changes instead of treating onPlaying as a one-shot trigger.
  // connectionEpoch changes on every (re)connect: the backend loses the
  // detection session when the hub connection is replaced, so the bookkeeping
  // is cleared and the requests are re-sent.
  useEffect(() => {
    if (!socket?.connected) return;
    detectionRequestedRef.current.clear();
  }, [connectionEpoch, socket]);

  useEffect(() => {
    if (liveCameraIds.size === 0 || !userName || !socket?.connected) return;

    cameras.filter((cam) => liveCameraIds.has(cam.id)).forEach((cam) => {
      if (detectionRequestedRef.current.has(cam.id)) return;

      // For webcam cameras, pass the HLS URL so the AI service reads
      // from FFmpeg's output instead of trying to open the device directly.
      let streamUrl = cam.stream_url;
      if (cam.stream_url?.startsWith('webcam:')) {
        streamUrl = `${API_CONFIG.BASE_URL}/streams/${cam.id}/index.m3u8`;
      }
      detectionRequestedRef.current.add(cam.id);
      sendDetectionRequest({
        stream_url: streamUrl,
        user: userName,
        location: cam.location,
        camera_name: cam.camera_name,
        camera_id: cam.id,
      });
    });
  }, [liveCameraIds, cameras, userName, socket, connectionEpoch, sendDetectionRequest]);

  const handleCameraPlaying = (cameraId: string) => {
    setLiveCameraIds((prev) => new Set(prev).add(cameraId));
  };

  const handleCameraStopped = (cameraId: string) => {
    detectionRequestedRef.current.delete(cameraId);
    setLiveCameraIds((prev) => {
      const next = new Set(prev);
      next.delete(cameraId);
      return next;
    });
    stopDetectionRequest(cameraId);
    StreamAPI.stop(cameraId).catch(() => {});
  };

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

    // Persist configured-camera removal before changing the visible list.
    const isLocal = cameraId.startsWith('webcam');
    if (!isLocal) {
      const result = await CamerasAPI.remove(cameraId);
      if (!result.success) {
        toast.error(result.error || 'Failed to remove camera');
        return;
      }
    }

    StreamAPI.stop(cameraId).catch(() => {});
    setCameras((prev) => prev.filter((c) => c.id !== cameraId));
    toast.success('Camera removed');
  };

  // Add a new camera
  const handleAddCamera = async () => {
    if (!newCamera.name || !newCamera.location) {
      toast.error('Please fill in required fields (Name, Location)');
      return;
    }
    if (newCamera.connectionMode === 'rtsp' && !/^rtsps?:\/\//i.test(newCamera.rtspUrl)) {
      toast.error('Please enter a valid RTSP URL');
      return;
    }
    if (newCamera.connectionMode === 'ip' && !newCamera.cameraIp) {
      toast.error('Please enter the camera IP address');
      return;
    }
    setAddLoading(true);
    try {
      const result = await CamerasAPI.add({
        name: newCamera.name,
        location: newCamera.location,
        rtspUrl: newCamera.connectionMode === 'rtsp' ? newCamera.rtspUrl : undefined,
        cameraIp: newCamera.connectionMode === 'ip' ? newCamera.cameraIp : undefined,
        cameraUsername: newCamera.connectionMode === 'ip' ? newCamera.cameraUsername || undefined : undefined,
        cameraPassword: newCamera.connectionMode === 'ip' ? newCamera.cameraPassword || undefined : undefined,
        cameraPort: newCamera.connectionMode === 'ip' ? newCamera.cameraPort || undefined : undefined,
        cameraBrand: newCamera.connectionMode === 'ip' ? newCamera.cameraBrand || undefined : undefined,
        cameraPath: newCamera.connectionMode === 'ip' ? newCamera.cameraPath || undefined : undefined,
      });
      if (result.success) {
        toast.success('Camera added!');
        setNewCamera({
          name: '',
          connectionMode: 'ip',
          rtspUrl: '',
          cameraIp: '',
          cameraUsername: '',
          cameraPassword: '',
          cameraPort: '',
          cameraBrand: '',
          cameraPath: '',
          location: '',
        });
        setShowAddForm(false);
        loadCameras();
      } else {
        toast.error(result.error || 'Failed to add camera');
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
            {detectionAlert.type === 'hit_list' ? 'Hit List Match' : 'Weapon Detected'}: {detectionAlert.weaponType} (
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
              onPlaying={() => handleCameraPlaying(cam.id)}
              onStopped={() => handleCameraStopped(cam.id)}
              onRemove={() => handleRemoveCamera(cam.id, cam.camera_name)}
            />
          ))}
        </div>
      )}

      {/* Status Badge */}
      <div className="flex items-center gap-2">
        <span
          className={`w-2.5 h-2.5 rounded-full ${liveCameraIds.size > 0 ? 'bg-emerald-400' : 'bg-red-400'}`}
        />
        <span className="text-xs font-bold text-slate-300 uppercase tracking-wider">
          {liveCameraIds.size > 0 ? 'LIVE' : 'OFFLINE'}
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
            Add Camera
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

          <div className="grid grid-cols-2 gap-2">
            {(['ip', 'rtsp'] as const).map((mode) => (
              <button
                key={mode}
                type="button"
                onClick={() => setNewCamera((prev) => ({ ...prev, connectionMode: mode }))}
                className={`rounded-lg px-3 py-2 text-sm font-medium ${newCamera.connectionMode === mode ? 'bg-primary text-white' : 'bg-slate-700/40 text-slate-300'}`}
              >
                {mode === 'ip' ? 'IP Camera' : 'Direct RTSP'}
              </button>
            ))}
          </div>

          {newCamera.connectionMode === 'rtsp' && (
            <input
              type="url"
              placeholder="RTSP URL *"
              className="input-field py-2 text-sm"
              value={newCamera.rtspUrl}
              onChange={(e) => setNewCamera((prev) => ({ ...prev, rtspUrl: e.target.value }))}
            />
          )}

          {[
            {
              icon: <HiVideoCamera size={16} />,
              ph: 'Camera Name *',
              field: 'name',
            },
            ...(newCamera.connectionMode === 'ip' ? [{
              icon: <HiSignal size={16} />,
              ph: 'Camera IP *',
              field: 'cameraIp',
            }] : []),
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
            ...(newCamera.connectionMode === 'ip' ? [{
              icon: <HiWifi size={16} />,
              ph: 'Port (default 554)',
              field: 'cameraPort',
            },
            {
              icon: <HiSignal size={16} />,
              ph: 'Stream path (optional)',
              field: 'cameraPath',
            }] : []),
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

          {newCamera.connectionMode === 'ip' && (
            <select
              aria-label="Camera brand"
              className="input-field py-2 text-sm"
              value={newCamera.cameraBrand}
              onChange={(e) => setNewCamera((prev) => ({ ...prev, cameraBrand: e.target.value }))}
            >
              <option value="">Generic camera</option>
              <option value="Hikvision">Hikvision</option>
              <option value="Dahua">Dahua</option>
              <option value="Meari">Meari</option>
            </select>
          )}

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
function playAlarmSound(type: 'weapon' | 'hit_list' | 'suspicious') {
  try {
    const AudioCtx = window.AudioContext || (window as any).webkitAudioContext;
    if (!AudioCtx) return;
    const ctx = new AudioCtx();

    if (type === 'weapon' || type === 'hit_list') {
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

// 1×1 transparent GIF. Assigning this to <img>.src is the reliable way to make
// the browser abort an in-flight multipart/x-mixed-replace request; `src = ''`
// leaves the connection open in Chromium, and stale MJPEG connections quickly
// exhaust the ~6-per-host budget so the next stream never loads.
const BLANK_IMAGE =
  'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';

function releaseMjpeg(img: HTMLImageElement | null) {
  if (!img) return;
  img.src = BLANK_IMAGE;
}

// How long a face box survives without a fresh detection. The AI resends each
// tracked face every ~0.2 s, so 1 s tolerates dropped updates while still
// clearing the box quickly once the person leaves. The old 8 s outlived the
// detection feed by a wide margin and left ghost boxes on an empty scene.
const FACE_BOX_TTL_MS = 1000;
// Fraction of the remaining distance the drawn box closes each animation frame.
const FACE_BOX_SMOOTHING = 0.25;

type Box = { x: number; y: number; w: number; h: number };

// True when two normalised boxes intersect at all. Used to spot a stale slot
// left behind by a re-identified person, so only a plain overlap test is needed.
function boxesOverlap(a: Box, b: Box): boolean {
  return a.x < b.x + b.w && b.x < a.x + a.w &&
         a.y < b.y + b.h && b.y < a.y + a.h;
}

interface CameraCardProps {
  camera: CameraData;
  onPlaying: () => void;
  onStopped: () => void;
  onRemove: () => void;
}

function CameraCard({ camera, onPlaying, onStopped, onRemove }: CameraCardProps) {
  const imgRef = useRef<HTMLImageElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  // Map of face boxes keyed by the AI's stable track label ("Person 1", …).
  // `bbox` is the latest box from the AI; `draw` is the smoothed box actually
  // rendered, eased toward `bbox` every frame so a ~5 Hz detection feed does not
  // look like it is teleporting on a 60 Hz canvas.
  const activeFacesRef = useRef<Map<string, {
    bbox: { x: number; y: number; w: number; h: number };
    draw: { x: number; y: number; w: number; h: number } | null;
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
  const onStoppedRef = useRef(onStopped);
  const [error, setError] = useState<string | null>(null);
  const [buffering, setBuffering] = useState(true);
  const mjpegReconnectTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const { socket } = useSocket();

  useEffect(() => {
    onPlayingRef.current = onPlaying;
    onStoppedRef.current = onStopped;
  }, [onPlaying, onStopped]);

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
      // Ease the drawn box toward the latest detection. Purely cosmetic and
      // free: it costs no inference, but turns a stepping box into a following
      // one. Snap instead of easing on first sight, and once close enough, so a
      // box never creeps forever toward its target.
      if (!f.draw) {
        f.draw = { ...f.bbox };
      } else {
        const d = f.draw;
        const settled =
          Math.abs(f.bbox.x - d.x) + Math.abs(f.bbox.y - d.y) +
          Math.abs(f.bbox.w - d.w) + Math.abs(f.bbox.h - d.h) < 0.002;
        if (settled) {
          f.draw = { ...f.bbox };
        } else {
          d.x += (f.bbox.x - d.x) * FACE_BOX_SMOOTHING;
          d.y += (f.bbox.y - d.y) * FACE_BOX_SMOOTHING;
          d.w += (f.bbox.w - d.w) * FACE_BOX_SMOOTHING;
          d.h += (f.bbox.h - d.h) * FACE_BOX_SMOOTHING;
        }
      }
      const { px, py, pw, ph, labelY } = toPx(f.draw.x, f.draw.y, f.draw.w, f.draw.h);
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

    // ── Draw alert overlay (weapon/hit-list=red, suspicious=yellow) ─
    const alert = activeAlertRef.current;
    if (alert) {
      if (now > alert.expires) {
        activeAlertRef.current = null;
      } else {
        const color = alert.type === 'weapon' || alert.type === 'hit_list' ? '#ef4444' : '#eab308';
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
        const previous = activeFacesRef.current.get(key);
        // If the AI re-identifies someone, the old label stops being refreshed
        // but survives its TTL, leaving a second box hanging where the person
        // used to be. Any other slot overlapping this box is that stale twin.
        for (const [otherKey, other] of activeFacesRef.current) {
          if (otherKey !== key && boxesOverlap(other.bbox, data.bbox)) {
            activeFacesRef.current.delete(otherKey);
          }
        }
        activeFacesRef.current.set(key, {
          bbox: data.bbox,
          // Keep the smoothed box across updates so it eases from where it was
          // drawn; a new person starts already at their real position.
          draw: previous?.draw ?? null,
          label: data.label,
          confidence: data.confidence,
          expires: Date.now() + FACE_BOX_TTL_MS,
        });
      } else {
        activeAlertRef.current = {
          type: data.type, label: data.label,
          confidence: data.confidence,
          bbox: data.bbox ?? null,
          expires: Date.now() + 3000,
        };
        if (data.sound === 'weapon')     playAlarmSound('weapon');
        if (data.sound === 'hit_list')   playAlarmSound('hit_list');
        if (data.sound === 'suspicious') playAlarmSound('suspicious');
      }
    };
    socket.on('detection-overlay', handler);
    return () => { socket.off('detection-overlay', handler); };
  }, [socket, camera.id]);

  // Use /streams/ path (bypasses /api/ rate limiter)
  const storedUser = UserStorage.getUser();
  const streamOwnerId = storedUser?.id ?? storedUser?._id;
  const hlsUrl = `${API_CONFIG.BASE_URL}/streams/${streamOwnerId}-${camera.id}/index.m3u8`;
  const streamToken = UserStorage.getToken();
  const mjpegBaseUrl = `${API_CONFIG.BASE_URL}/api/stream/mjpeg/${camera.id}${
    streamToken ? `?access_token=${encodeURIComponent(streamToken)}` : '?'
  }`;
  // Every (re)connect gets a unique URL so the browser opens a fresh socket
  // instead of reusing/queueing behind the previous multipart response.
  const nextMjpegUrl = useCallback(
    () => `${mjpegBaseUrl}${mjpegBaseUrl.endsWith('?') ? '' : '&'}t=${Date.now()}`,
    [mjpegBaseUrl],
  );

  // ── MJPEG reconnect on error ──────────────────────────────────────
  // Called when the browser's <img> HTTP connection to the MJPEG endpoint
  // drops (FFmpeg restarted, network blip, etc.). Schedules a fresh
  // connection with an incremented cache-bust parameter so the browser
  // doesn't serve a stale response from its cache.
  const mjpegRetryCount = useRef(0);
  const handleMjpegError = useCallback(() => {
    if (!imgRef.current || buffering) return;
    onStoppedRef.current();
    setError('Camera display disconnected. Stream activity was stopped.');
    if (mjpegReconnectTimer.current) clearTimeout(mjpegReconnectTimer.current);
    releaseMjpeg(imgRef.current);
  }, [camera.id, buffering]);

  // Reset retry counter when stream loads successfully
  const handleMjpegLoad = useCallback(() => {
    mjpegRetryCount.current = 0;
    setError(null);
    setBuffering(false);
    onPlayingRef.current();
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
          setError(null);
          setBuffering(false);
          onPlayingRef.current();
          if (imgRef.current) imgRef.current.src = nextMjpegUrl();
        } else if (attempt >= maxAttempts) {
          clearInterval(pollTimer);
          onStoppedRef.current();
          setError('Stream took too long to start. Try refreshing.');
          setBuffering(false);
        }
      } catch {
        if (attempt >= maxAttempts) {
          clearInterval(pollTimer);
          onStoppedRef.current();
          setError('Stream took too long to start. Try refreshing.');
          setBuffering(false);
        }
      }
    }, 500);

    return () => {
      destroyed = true;
      clearInterval(pollTimer);
      releaseMjpeg(imgRef.current);
      if (mjpegReconnectTimer.current) clearTimeout(mjpegReconnectTimer.current);
    };
  }, [hlsUrl, nextMjpegUrl, camera.id]);

  const retry = useCallback(async () => {
    setError(null);
    setBuffering(true);
    releaseMjpeg(imgRef.current);
    try {
      if (camera.stream_url.startsWith('webcam:')) {
        await StreamAPI.startWebcam(camera.id, camera.stream_url.slice('webcam:'.length));
      } else {
        await StreamAPI.start(camera.id, camera.stream_url);
      }
    } catch { /* ignore */ }
    let attempt = 0;
    const timer = setInterval(async () => {
      attempt++;
      try {
        const res = await fetch(hlsUrl, { method: 'HEAD' });
        if (res.ok) {
          clearInterval(timer);
          setError(null);
          setBuffering(false);
          onPlayingRef.current();
          if (imgRef.current) imgRef.current.src = nextMjpegUrl();
        }
      } catch { /* keep polling */ }
      if (attempt >= 30) {
        clearInterval(timer);
        onStoppedRef.current();
        setError('Stream unavailable. The camera may be offline.');
        setBuffering(false);
      }
    }, 500);
  }, [hlsUrl, nextMjpegUrl, camera.id, camera.stream_url]);

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
