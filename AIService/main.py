"""
AttackShield AI Service
Multi-Model Ensemble:
  1. YOLOv10  – Weapon Detection (knife, pistol, gun)
  2. I3D (Inflated 3D ConvNet) – Suspicious Activity / Action Recognition
  3. RetinaFace – High-accuracy Face Detection
Aligns with the AttackShield AI project proposal.
"""

from fastapi import FastAPI, BackgroundTasks
from pydantic import BaseModel
import cv2
import numpy as np
from ultralytics import YOLO
import requests
import time
from datetime import datetime
import os
import threading
import subprocess
import shutil
import glob
import asyncio
from collections import deque

import torch
import torch.nn.functional as F
from torchvision.models.video import r3d_18, R3D_18_Weights
from torchvision import transforms
from retinaface import RetinaFace as RF

app = FastAPI(title="AttackShield AI Service")

BASE_DIR = os.path.dirname(__file__)
MODEL_PATH = os.path.abspath(os.path.join(BASE_DIR, '..', 'Yolov10', 'model.pt'))
BACKEND_URL = os.environ.get(
    'BACKEND_URL',
    "http://localhost:5000/api/detections/receive"
)

# ── Pre-load all models when the service starts ────────────────────────
# Model loading (especially I3D weight download) can take 30-120 s.
# Running it at startup in a background thread means /start-detection
# returns in <100 ms instead of blocking until models are ready.
@app.on_event("startup")
async def preload_models():
    loop = asyncio.get_event_loop()
    print("[Startup] Pre-loading all AI models in background thread...")
    async def _load():
        await loop.run_in_executor(None, load_yolo_model)
        await loop.run_in_executor(None, load_i3d_model)
        await loop.run_in_executor(None, load_retinaface)
        print("[Startup] ✅ All models loaded and ready!")
    asyncio.create_task(_load())

CONFIDENCE_THRESHOLD = 0.25            # Low = catch weapons on webcam frames
FACE_DETECTION_INTERVAL = 2            # Run face detection every 2 frames
ACTIVITY_DETECTION_INTERVAL = 8        # Run I3D every 8 frames (half-clip overlap)

# Per-type deduplication windows (seconds)
DUPLICATE_WINDOWS = {
    "weapon":             10,   # Resend weapon alert every 10 s
    "suspicious_activity": 30,  # Resend activity alert every 30 s
    "face":               3,    # Resend face bbox every 3 s (keeps overlay alive)
}
I3D_CLIP_LENGTH = 16                   # Number of frames per I3D clip
I3D_CONFIDENCE_THRESHOLD = 0.20        # Lowered – broader activity sensitivity on CPU


# ═══════════════════════════════════════════════════════════════════
# Face Tracker – IoU-based persistent identity assignment
# ═══════════════════════════════════════════════════════════════════

class FaceTracker:
    """
    Greedy IoU-based face tracker.
    Assigns persistent IDs (Person 1, Person 2, …) to detected faces by
    comparing bounding-box IoU across frames so that the same person always
    keeps the same label throughout a detection session.
    """

    def __init__(self, iou_threshold: float = 0.25, max_age: int = 30):
        self._tracks: dict = {}         # track_id → {bbox, age}
        self._next_id: int = 1
        self._iou_threshold = iou_threshold
        self._max_age = max_age         # frames before a track is considered gone
        self._lock = threading.Lock()

    # ── helpers ───────────────────────────────────────────────────
    @staticmethod
    def _iou(a: dict, b: dict) -> float:
        ax1, ay1 = a["x"], a["y"]
        ax2, ay2 = ax1 + a["w"], ay1 + a["h"]
        bx1, by1 = b["x"], b["y"]
        bx2, by2 = bx1 + b["w"], by1 + b["h"]
        ix1, iy1 = max(ax1, bx1), max(ay1, by1)
        ix2, iy2 = min(ax2, bx2), min(ay2, by2)
        if ix2 <= ix1 or iy2 <= iy1:
            return 0.0
        inter = (ix2 - ix1) * (iy2 - iy1)
        union = a["w"] * a["h"] + b["w"] * b["h"] - inter
        return inter / union if union > 0 else 0.0

    # ── public API ────────────────────────────────────────────────
    def update(self, detections: list) -> list:
        """
        Match detections to existing tracks and assign stable labels.
        Unmatched detections create new tracks.
        Returns a new list with 'label' set to 'Person N'.
        """
        with self._lock:
            # Age existing tracks; remove stale ones
            for tid in list(self._tracks):
                self._tracks[tid]["age"] += 1
                if self._tracks[tid]["age"] > self._max_age:
                    del self._tracks[tid]

            labeled = []
            used_tids: set = set()

            for det in detections:
                if "bbox" not in det:
                    labeled.append(dict(det))
                    continue

                best_tid, best_iou = None, self._iou_threshold
                for tid, track in self._tracks.items():
                    if tid in used_tids:
                        continue
                    score = self._iou(det["bbox"], track["bbox"])
                    if score > best_iou:
                        best_iou, best_tid = score, tid

                if best_tid is not None:
                    # Update matched track
                    self._tracks[best_tid]["bbox"] = det["bbox"]
                    self._tracks[best_tid]["age"]  = 0
                    used_tids.add(best_tid)
                    label = f"Person {best_tid}"
                else:
                    # Register new person
                    tid = self._next_id
                    self._next_id += 1
                    self._tracks[tid] = {"bbox": det["bbox"], "age": 0}
                    used_tids.add(tid)
                    label = f"Person {tid}"

                new_det = dict(det)
                new_det["label"] = label
                labeled.append(new_det)

            return labeled

    def reset(self):
        with self._lock:
            self._tracks.clear()
            self._next_id = 1

    @property
    def unique_count(self) -> int:
        """Total unique persons ever seen in this session (never decreases)."""
        with self._lock:
            return self._next_id - 1


# ── Suspicious-activity keyword matching ────────────────────────────
# R3D-18 (Kinetics-400) predicts exact category strings like
# "punching person (boxing)", "wrestling", "archery", etc.
# We match by substring so small wording differences are tolerated, and we
# also catch future / variant label spellings.
#
# Rules:
#  • Keep keywords SHORT enough to be substrings of real Kinetics labels.
#  • Do NOT add words that are too generic ("run", "carry") to avoid false
#    positives on innocent actions.
#  • Every keyword is lowercased; labels are lowercased before matching.
SUSPICIOUS_KEYWORDS = [
    # ── Physical violence ──────────────────────────────────────────
    "punch",          # "punching person (boxing)", "punching bag"
    "wrestl",         # "wrestling"
    "headbutt",       # "headbutting"
    "slap",           # "slapping"
    "sword",          # "sword fighting"
    "drop kick",      # "drop kicking"
    "fencing",        # "fencing (sport)"
    # ── Weapons / throwing ─────────────────────────────────────────
    "archery",
    "throwing axe",
    "javelin",
    "shot put",
    "hammer throw",
    "golf driv",      # "golf driving" (swing similar to bludgeoning)
    # ── Rapid / escape movement ────────────────────────────────────
    "jogging",        # actual Kinetics-400 label
    "parkour",
    "bungee",
    "skydiving",
    "abseiling",
    "rock climbing",
    "hurdling",
    # ── Vandalism / crime signals ──────────────────────────────────
    "spray paint",    # "spray painting" — graffiti / vandalism
    "graffiti",
    # ── Weapon-adjacent sport moves (high-alert context) ───────────
    "arm wrestling",
    "playing paintball",
    "tai chi",        # martial-arts warm-up
    # ── Carrying / dragging people ─────────────────────────────────
    "carrying baby",
    "dragging",
    # ── Direct Kinetics-400 violent-sport labels ───────────────────
    "punching bag",
    "shooting goal",  # rapid charging motion
    "pushing wheelchair",
    "pushing cart",
]

# Pre-compute lowercase keywords once at import time (minor speed gain).
_SUSPICIOUS_KW_LOWER = [kw.lower() for kw in SUSPICIOUS_KEYWORDS]

# ── Models ──────────────────────────────────────────────────────────
yolo_model = None
i3d_model = None
i3d_weights = None
i3d_categories = None
i3d_preprocess = None
retinaface_loaded = False
detection_active = False
last_detections = {}

# ── Face Tracker (singleton, reset per session) ──────────────────
face_tracker = FaceTracker(iou_threshold=0.25, max_age=30)

latest_frame = None
frame_lock = threading.Lock()

# ── I3D frame buffer ───────────────────────────────────────────────
i3d_buffer_lock = threading.Lock()
i3d_frame_buffer = deque(maxlen=I3D_CLIP_LENGTH)

# ── Trust Score state ───────────────────────────────────────────────
trust_score = {
    "score": 92.0,
    "auth_consistency": 100.0,
    "anomaly_frequency": 0.0,
    "model_confidence_stability": 95.0,
    "communication_integrity": 100.0,
    "policy_compliance": 100.0,
}

# ── Ensemble metrics state ──────────────────────────────────────────
ensemble_metrics = {
    "weapons_detected": 0,
    "suspicious_activities": 0,
    "faces_detected": 0,
    "unique_persons": 0,         # total unique persons seen this session
    "total_frames_processed": 0,
    "avg_inference_latency_ms": 0.0,
    "ensemble_confidence": 0.0,
}
metrics_lock = threading.Lock()


class DetectionRequest(BaseModel):
    rtsp_url: str
    location: str
    user_id: str | None = None
    camera_id: str | None = None
    camera_name: str | None = None


# ═══════════════════════════════════════════════════════════════════
# Model Loaders
# ═══════════════════════════════════════════════════════════════════

def load_yolo_model():
    """Load YOLOv10 weapon detection model."""
    global yolo_model
    if yolo_model is None:
        print(f"[YOLO] Loading weapon model from {MODEL_PATH}")
        yolo_model = YOLO(MODEL_PATH)
        print("[YOLO] Weapon model loaded")


def load_i3d_model():
    """
    Load I3D (Inflated 3D ConvNet) for action recognition.
    Uses torchvision's R3D-18 pretrained on Kinetics-400.
    R3D-18 is an I3D-family model (3D ResNet with inflated convolutions).
    """
    global i3d_model, i3d_weights, i3d_categories, i3d_preprocess

    if i3d_model is not None:
        return

    print("[I3D] Loading action recognition model (R3D-18, Kinetics-400)...")
    i3d_weights = R3D_18_Weights.KINETICS400_V1
    i3d_model = r3d_18(weights=i3d_weights)
    i3d_model.eval()

    # Move to GPU if available
    if torch.cuda.is_available():
        i3d_model = i3d_model.cuda()
        print("   I3D running on CUDA GPU")
    else:
        print("   I3D running on CPU")

    i3d_categories = i3d_weights.meta["categories"]
    i3d_preprocess = i3d_weights.transforms()
    print(f"[I3D] Model loaded - {len(i3d_categories)} action categories")


def load_retinaface():
    """
    RetinaFace initialization.
    The retinaface package auto-downloads its weights on first use.
    We just set a flag so we know it's ready.
    """
    global retinaface_loaded
    if retinaface_loaded:
        return
    print("[RetinaFace] Initializing face detector...")
    # Warm-up: run one detection on a dummy image to trigger weight download
    dummy = np.zeros((112, 112, 3), dtype=np.uint8)
    RF.detect_faces(dummy)
    retinaface_loaded = True
    print("[RetinaFace] Model loaded")


# ═══════════════════════════════════════════════════════════════════
# Detection Functions (Ensemble Components)
# ═══════════════════════════════════════════════════════════════════

def detect_weapons(frame):
    """YOLOv10-based weapon detection – detects all classes in the weapon model."""
    results = yolo_model(frame, conf=CONFIDENCE_THRESHOLD, verbose=False)
    detections = []
    fh, fw = frame.shape[:2]

    for result in results:
        if not hasattr(result, 'boxes') or result.boxes is None:
            continue
        for box in result.boxes:
            cls  = int(box.cls)
            conf = float(box.conf)
            class_name = yolo_model.names[cls]

            # Accept all classes except the negative/background class
            # 'non-pistol' = gun/rifle that isn’t a pistol – still a weapon
            display_label = class_name
            if class_name.lower() == 'non-pistol':
                display_label = 'Gun/Rifle'

            x1, y1, x2, y2 = [float(v) for v in box.xyxy[0]]
            detections.append({
                "type": "weapon",
                "label": display_label,
                "confidence": conf,
                "bbox": {
                    "x": round(x1 / fw, 4),
                    "y": round(y1 / fh, 4),
                    "w": round((x2 - x1) / fw, 4),
                    "h": round((y2 - y1) / fh, 4),
                },
            })
            print(f"[Weapon] Detected: {display_label} ({conf:.0%})")

    return detections


def detect_suspicious_activity_i3d():
    """
    I3D-based suspicious activity detection using R3D-18 (Inflated 3D ConvNet).
    Classifies a clip of 16 frames into one of 400 Kinetics action classes.
    If the top prediction is in the SUSPICIOUS_ACTIONS set, we flag it.
    """
    detections = []

    with i3d_buffer_lock:
        if len(i3d_frame_buffer) < I3D_CLIP_LENGTH:
            return detections
        clip_frames = list(i3d_frame_buffer)

    # Prepare clip: resize → uint8 tensor → (C, T, H, W) → preprocess
    # IMPORTANT: R3D_18_Weights.transforms() (VideoClassification) expects
    # uint8 tensors in range [0, 255] shaped (C, T, H, W). Do NOT divide by
    # 255 here – the transform handles normalisation internally.
    processed = []
    for frame in clip_frames:
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        resized = cv2.resize(rgb, (112, 112))
        # Keep as uint8 – the transform expects [0, 255]
        tensor = torch.from_numpy(resized).permute(2, 0, 1)   # (C, H, W) uint8
        processed.append(tensor)

    # Stack to (T, C, H, W) then permute to (C, T, H, W)
    clip_tensor = torch.stack(processed, dim=0)          # (T, C, H, W) uint8
    clip_tensor = clip_tensor.permute(1, 0, 2, 3)        # (C, T, H, W) uint8

    # Apply the pretrained transforms (resize + crop + normalize to float)
    clip_tensor = i3d_preprocess(clip_tensor)

    # Add batch dimension
    batch = clip_tensor.unsqueeze(0)                      # (1, C, T, H, W)
    if torch.cuda.is_available():
        batch = batch.cuda()

    with torch.no_grad():
        logits = i3d_model(batch)
        probs = F.softmax(logits, dim=1)
        top5_probs, top5_indices = probs.topk(5, dim=1)

    # Always log the top-3 so we can see what the model is actually predicting
    # (visible in the AI service terminal — helps diagnose missed detections).
    top3_str = ", ".join(
        f"{i3d_categories[top5_indices[0, i].item()]} ({top5_probs[0, i].item():.0%})"
        for i in range(3)
    )
    print(f"[I3D] Top-3: {top3_str}")

    for i in range(5):
        idx   = top5_indices[0, i].item()
        prob  = top5_probs[0, i].item()
        label = i3d_categories[idx]
        label_lower = label.lower()

        # Keyword-based matching: tolerates minor label differences and catches
        # variant phrasing that exact-set matching misses.
        is_suspicious = any(kw in label_lower for kw in _SUSPICIOUS_KW_LOWER)

        if is_suspicious and prob >= I3D_CONFIDENCE_THRESHOLD:
            print(f"[I3D] Suspicious activity: {label} ({prob:.0%})")
            detections.append({
                "type": "suspicious_activity",
                "label": f"Suspicious: {label}",
                "confidence": round(prob, 3),
                "action_class": label,
                "model": "I3D (R3D-18)",
            })

    return detections


def detect_faces_retinaface(frame):
    """
    RetinaFace deep-learning face detection.
    Returns bounding boxes + confidence for every detected face.
    Frame is downscaled before inference for speed, then bbox is re-scaled back.
    """
    detections = []
    fh, fw = frame.shape[:2]

    # Downscale to max 480px wide while keeping aspect ratio – significantly
    # speeds up RetinaFace without hurting face-detection accuracy.
    scale = min(1.0, 480 / fw)
    if scale < 1.0:
        small = cv2.resize(frame, (int(fw * scale), int(fh * scale)))
    else:
        small = frame
    sh, sw = small.shape[:2]

    try:
        result = RF.detect_faces(small)
    except Exception as e:
        print(f"[RetinaFace] Error: {e}")
        return detections

    if not isinstance(result, dict):
        return detections

    for face_key, face_data in result.items():
        conf = float(face_data.get("score", 0.0))
        if conf < 0.40:   # Match backend face threshold (was 0.45)
            continue

        facial_area = face_data.get("facial_area", [0, 0, 0, 0])
        x1, y1, x2, y2 = facial_area
        # Scale bbox back to original frame dimensions, then normalize to 0-1
        nx  = (x1 / sw) if scale < 1.0 else (x1 / fw)
        ny  = (y1 / sh) if scale < 1.0 else (y1 / fh)
        nw  = ((x2 - x1) / sw) if scale < 1.0 else ((x2 - x1) / fw)
        nh  = ((y2 - y1) / sh) if scale < 1.0 else ((y2 - y1) / fh)
        detections.append({
            "type": "face",
            "label": "Person",       # will be overwritten by tracker below
            "confidence": round(conf, 3),
            "bbox": {
                "x": round(nx, 4),
                "y": round(ny, 4),
                "w": round(nw, 4),
                "h": round(nh, 4),
            },
            "model": "RetinaFace",
        })

    # Assign persistent person IDs (Person 1, Person 2, …)
    return face_tracker.update(detections)


# ═══════════════════════════════════════════════════════════════════
# Ensemble Decision Engine
# ═══════════════════════════════════════════════════════════════════

def ensemble_decision(weapon_dets, activity_dets, face_dets):
    """
    Aggregate detections from all models and compute ensemble confidence.
    Threat alerts are generated when ensemble confidence exceeds a threshold.
    """
    all_detections = []
    confidences = []

    for d in weapon_dets:
        d["priority"] = "high"
        d["threat_level"] = "critical"
        all_detections.append(d)
        confidences.append(d["confidence"])

    for d in activity_dets:
        d["priority"] = "medium"
        d["threat_level"] = "warning"
        all_detections.append(d)
        confidences.append(d["confidence"])

    for d in face_dets:
        d["priority"] = "low"
        d["threat_level"] = "info"
        all_detections.append(d)
        confidences.append(d["confidence"])

    ensemble_conf = float(np.mean(confidences)) if confidences else 0.0

    return all_detections, round(ensemble_conf, 3)


# ═══════════════════════════════════════════════════════════════════
# Backend Communication
# ═══════════════════════════════════════════════════════════════════

def send_detection_to_backend(detection, location, confidence, user_id=None, camera_id=None, camera_name=None):
    det_type = detection.get("type", "weapon")
    label = detection.get("label", "Unknown")

    payload = {
        "weaponType": label,
        "detectionType": det_type,       # weapon | suspicious_activity | face
        "location": location,
        "confidence": confidence,
        "userId": user_id,
        "cameraId": camera_id,
        "cameraName": camera_name,
        "bbox": detection.get("bbox"),   # normalized 0-1 bbox (if available)
        "priority": detection.get("priority", "medium"),
        "threatLevel": detection.get("threat_level", "warning"),
        "ensembleModel": True,
    }

    try:
        response = requests.post(BACKEND_URL, json=payload, timeout=5)
        if response.status_code == 200:
            print(f"[Detection] Sent: [{det_type}] {label} ({confidence:.0%})")
        else:
            print("[Detection] Backend error:", response.text)
    except Exception as e:
        print("[Detection] Request failed:", e)


# ═══════════════════════════════════════════════════════════════════
# Stream Processing (Multi-Model Pipeline)
# ═══════════════════════════════════════════════════════════════════

def _find_ffmpeg() -> str:
    """Locate the FFmpeg binary: PATH first, then common winget install path."""
    found = shutil.which("ffmpeg")
    if found:
        return found
    # Common winget install location on Windows
    user = os.environ.get("USERPROFILE", "")
    pattern = os.path.join(
        user, "AppData", "Local", "Microsoft", "WinGet",
        "Packages", "Gyan.FFmpeg*", "*", "bin", "ffmpeg.exe",
    )
    matches = glob.glob(pattern)
    if matches:
        return matches[0]
    return "ffmpeg"   # last resort: hope it's on PATH


FFMPEG_BIN = _find_ffmpeg()
print(f"[AI] Using FFmpeg at: {FFMPEG_BIN}")

# ── Frame dimensions that match FFmpeg output ──────────────────────
# Must match the '-s' flag used in _open_ffmpeg_pipe below.
GRAB_W, GRAB_H = 640, 480


def _open_ffmpeg_pipe(url: str):
    """
    Open an FFmpeg subprocess that decodes ANY stream (HLS, RTSP, webcam …)
    and pipes raw BGR frames to stdout.  Returns (subprocess, frame_size).
    This avoids OpenCV's poor HLS support and H.264 PPS/SPS handling.
    """

    is_hls = url.startswith("http://") or url.startswith("https://")

    cmd = [FFMPEG_BIN,
           "-loglevel", "error",          # suppress noisy decoder warnings
           # ── input flags ──────────────────────────────────────────
           "-fflags", "+discardcorrupt+genpts",
           ]

    if is_hls:
        cmd += [
            "-allowed_extensions", "ALL",
            "-protocol_whitelist", "file,http,https,tcp,tls,crypto",
            # Reconnect on dropped HLS segments
            "-reconnect", "1",
            "-reconnect_streamed", "1",
            "-reconnect_delay_max", "5",
        ]
    else:
        # RTSP
        cmd += ["-rtsp_transport", "tcp"]

    cmd += [
        "-i", url,
        # ── output: raw BGR frames piped to stdout ───────────────────
        "-f",       "rawvideo",
        "-pix_fmt", "bgr24",
        "-s",       f"{GRAB_W}x{GRAB_H}",
        "-r",       "15",       # cap to 15 fps – matches the HLS encode rate
        "-an",                  # no audio
        "pipe:1",
    ]

    proc = subprocess.Popen(
        cmd,
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,   # discard FFmpeg's own error output
        bufsize=0,
    )
    frame_size = GRAB_W * GRAB_H * 3   # bytes per raw BGR frame
    return proc, frame_size


def frame_grabber(rtsp_url):
    global latest_frame, detection_active

    print(f"[Stream] Opening stream: {rtsp_url}")

    # ── Choose strategy based on URL type ────────────────────────────
    # HTTP MJPEG multipart streams – cv2.VideoCapture handles these natively
    #   and is far simpler + more reliable than FFmpeg for MJPEG.
    # RTSP streams – use FFmpeg pipe (handles H.264 SPS/PPS correctly).
    is_http = rtsp_url.startswith("http://") or rtsp_url.startswith("https://")

    if is_http:
        _frame_grabber_mjpeg(rtsp_url)
    else:
        _frame_grabber_ffmpeg(rtsp_url)


def _frame_grabber_mjpeg(url):
    """OpenCV VideoCapture-based grabber for HTTP MJPEG multipart streams."""
    global latest_frame, detection_active

    max_retries = 8
    retry_delay = 2.0
    cap = None

    for attempt in range(1, max_retries + 1):
        if not detection_active:
            return
        cap = cv2.VideoCapture(url)
        if cap.isOpened():
            # Confirm at least one frame is actually readable
            ret, test_frame = cap.read()
            if ret and test_frame is not None:
                with frame_lock:
                    latest_frame = test_frame
                break
            cap.release()
            cap = None
        print(f"[Stream] MJPEG not ready (attempt {attempt}/{max_retries}), "
              f"retrying in {retry_delay:.0f}s…")
        time.sleep(retry_delay)
        retry_delay = min(retry_delay * 1.3, 8)

    if cap is None:
        print("[Stream] Unable to open MJPEG stream after all retries")
        detection_active = False
        return

    print("[Stream] Frame grabber started (MJPEG/OpenCV)")

    consecutive_failures = 0
    while detection_active:
        ret, frame = cap.read()
        if not ret or frame is None:
            consecutive_failures += 1
            if consecutive_failures > 30:
                print("[Stream] MJPEG read failures — attempting reconnect…")
                cap.release()
                time.sleep(2)
                cap = cv2.VideoCapture(url)
                if not cap.isOpened():
                    print("[Stream] MJPEG reconnect failed")
                    detection_active = False
                    break
                consecutive_failures = 0
                print("[Stream] Reconnected to MJPEG stream")
            continue

        consecutive_failures = 0
        with frame_lock:
            latest_frame = frame

    cap.release()
    print("[Stream] Frame grabber stopped")


def _frame_grabber_ffmpeg(rtsp_url):
    """FFmpeg-pipe grabber for RTSP streams."""
    global latest_frame, detection_active

    print(f"[Stream] Opening RTSP stream via FFmpeg pipe: {rtsp_url}")

    max_retries = 5
    retry_delay = 2.0
    proc = None
    frame_size = GRAB_W * GRAB_H * 3

    for attempt in range(1, max_retries + 1):
        if not detection_active:
            return
        try:
            proc, frame_size = _open_ffmpeg_pipe(rtsp_url)
            test = proc.stdout.read(frame_size)
            if len(test) == frame_size:
                first = np.frombuffer(test, dtype=np.uint8).reshape(GRAB_H, GRAB_W, 3)
                with frame_lock:
                    latest_frame = first.copy()
                break
            proc.kill(); proc = None
        except Exception as e:
            print(f"[Stream] FFmpeg open error (attempt {attempt}): {e}")
            if proc:
                proc.kill(); proc = None

        print(f"[Stream] Unable to open RTSP stream (attempt {attempt}/{max_retries}), "
              f"retrying in {retry_delay:.0f}s…")
        time.sleep(retry_delay)
        retry_delay = min(retry_delay * 1.5, 10)

    if proc is None:
        print("[Stream] Unable to open RTSP stream after all retries")
        detection_active = False
        return

    print("[Stream] Frame grabber started (RTSP/FFmpeg pipe)")

    consecutive_failures = 0
    while detection_active:
        try:
            raw = proc.stdout.read(frame_size)
        except Exception:
            break

        if len(raw) != frame_size:
            consecutive_failures += 1
            if consecutive_failures > 30:
                print("[Stream] Too many short reads — attempting reconnect…")
                proc.kill(); proc = None
                time.sleep(2)
                try:
                    proc, frame_size = _open_ffmpeg_pipe(rtsp_url)
                    consecutive_failures = 0
                    print("[Stream] Reconnected to RTSP stream")
                except Exception as e:
                    print(f"[Stream] Reconnect failed: {e}")
                    detection_active = False
                    break
            continue

        consecutive_failures = 0
        frame = np.frombuffer(raw, dtype=np.uint8).reshape(GRAB_H, GRAB_W, 3)
        with frame_lock:
            latest_frame = frame.copy()

    if proc:
        proc.kill()
    print("[Stream] Frame grabber stopped")


def process_stream(rtsp_url, location, user_id=None, camera_id=None, camera_name=None):
    """
    Main ensemble detection loop.
    - Weapon detection (YOLOv10) runs on every frame – maximum speed.
    - Face and activity detection run in dedicated background threads so
      they never block or slow down the weapon detection loop.
    """
    global detection_active, last_detections, ensemble_metrics, trust_score

    print("[AttackShield AI] Ensemble detection started")

    grabber_thread = threading.Thread(
        target=frame_grabber,
        args=(rtsp_url,),
        daemon=True,
    )
    grabber_thread.start()

    # ── Shared result lists for background detectors ──
    pending_face_dets: list = []
    face_lock = threading.Lock()
    pending_activity_dets: list = []
    activity_lock = threading.Lock()

    def face_worker():
        """Run RetinaFace continuously on the latest frame (~every 100 ms)."""
        while detection_active:
            try:
                with frame_lock:
                    f = latest_frame
                if f is not None:
                    dets = detect_faces_retinaface(f)
                    with face_lock:
                        pending_face_dets.clear()
                        pending_face_dets.extend(dets)
            except Exception as e:
                print(f"[FaceWorker] Error (continuing): {e}")
            time.sleep(0.1)   # ~10 fps face detection

    def activity_worker():
        """Run I3D whenever the clip buffer is full (~every 0.5 s on 15 fps)."""
        while detection_active:
            try:
                dets = detect_suspicious_activity_i3d()
                if dets:
                    with activity_lock:
                        pending_activity_dets.clear()
                        pending_activity_dets.extend(dets)
            except Exception as e:
                print(f"[ActivityWorker] Error (continuing): {e}")
            time.sleep(0.5)

    threading.Thread(target=face_worker,     daemon=True).start()
    threading.Thread(target=activity_worker, daemon=True).start()

    frame_count = 0
    latency_window = deque(maxlen=100)
    last_frame_id: int = 0        # Python id() of the last processed frame

    while detection_active:
        with frame_lock:
            frame = latest_frame

        if frame is None:
            time.sleep(0.01)
            continue

        # ── Skip duplicate frames ──────────────────────────────────────
        # The frame_grabber runs in its own thread and updates latest_frame
        # when a new frame arrives from the stream.  Without this guard,
        # the detection loop (YOLO is fast on GPU) can run YOLO multiple
        # times on the same frame – wasting CPU and filling the I3D
        # buffer with identical frames which corrupts clip-based inference.
        current_frame_id = id(frame)
        if current_frame_id == last_frame_id:
            time.sleep(0.005)     # yield CPU; new frame expected in ~67 ms
            continue
        last_frame_id = current_frame_id

        frame_count += 1
        t_start = time.time()

        # ── 1. Weapon Detection – YOLOv10 (every frame, no blocking) ──
        weapon_dets = detect_weapons(frame)

        # ── 2. Feed I3D clip buffer ──
        with i3d_buffer_lock:
            i3d_frame_buffer.append(frame.copy())

        # ── 3. Consume latest results from background threads ──
        with face_lock:
            face_dets = list(pending_face_dets)
        with activity_lock:
            activity_dets = list(pending_activity_dets)
            if activity_dets:
                pending_activity_dets.clear()   # consume once so we don't re-alert

        # ── 4. Ensemble Decision ──
        all_dets, ens_conf = ensemble_decision(weapon_dets, activity_dets, face_dets)

        t_end = time.time()
        latency_ms = (t_end - t_start) * 1000
        latency_window.append(latency_ms)

        # ── 5. Update metrics ──
        with metrics_lock:
            ensemble_metrics["total_frames_processed"] = frame_count
            ensemble_metrics["weapons_detected"] += len(weapon_dets)
            ensemble_metrics["suspicious_activities"] += len(activity_dets)
            ensemble_metrics["faces_detected"] += len(face_dets)
            ensemble_metrics["unique_persons"] = face_tracker.unique_count
            ensemble_metrics["avg_inference_latency_ms"] = round(
                float(np.mean(latency_window)), 2
            )
            ensemble_metrics["ensemble_confidence"] = ens_conf

        # ── 6. Update trust score ──
        if all_dets:
            confs = [d["confidence"] for d in all_dets]
            conf_std = float(np.std(confs)) if len(confs) > 1 else 0.0
            trust_score["model_confidence_stability"] = round(
                max(100.0 - conf_std * 100, 0), 1
            )
            trust_score["anomaly_frequency"] = round(
                min(len(all_dets) * 2.5, 50.0), 1
            )
            trust_score["score"] = round(
                (
                    trust_score["auth_consistency"]
                    + (100 - trust_score["anomaly_frequency"])
                    + trust_score["model_confidence_stability"]
                    + trust_score["communication_integrity"]
                    + trust_score["policy_compliance"]
                ) / 5.0,
                1,
            )

        # ── 7. Send detections to backend (per-type deduplication) ──
        current_time = time.time()

        for det in all_dets:
            # For faces, key by person label ("face:Person 1", "face:Person 2", ...)
            # so each unique person has its own independent dedup window.
            if det["type"] == "face":
                det_key = f"face:{det.get('label', 'unknown')}"
            else:
                det_key = f"{det['type']}:{det['label']}"
            confidence = det["confidence"]
            dedup_window = DUPLICATE_WINDOWS.get(det["type"], 10)

            if det_key in last_detections:
                if current_time - last_detections[det_key] < dedup_window:
                    continue

            last_detections[det_key] = current_time
            send_detection_to_backend(det, location, confidence, user_id, camera_id, camera_name)

    print("[AttackShield AI] Ensemble detection stopped")


# ═══════════════════════════════════════════════════════════════════
# API Endpoints
# ═══════════════════════════════════════════════════════════════════

@app.post("/start-detection")
async def start_detection(request: DetectionRequest, background_tasks: BackgroundTasks):
    global detection_active, latest_frame

    # Stop any running session
    if detection_active:
        print("🔄 Stopping previous detection before restarting...")
        detection_active = False
        await asyncio.sleep(0.5)   # brief pause – threads see the flag change

    # If models aren’t loaded yet (e.g. startup hasn’t finished), load them
    # now in a thread-pool executor so we don’t block the event loop.
    if yolo_model is None or i3d_model is None or not retinaface_loaded:
        print("[Detection] Models not ready yet – loading now (non-blocking)...")
        loop = asyncio.get_event_loop()
        await loop.run_in_executor(None, load_yolo_model)
        await loop.run_in_executor(None, load_i3d_model)
        await loop.run_in_executor(None, load_retinaface)

    detection_active = True
    last_detections.clear()
    face_tracker.reset()             # Start fresh person IDs for each session
    with metrics_lock:
        ensemble_metrics["unique_persons"] = 0
    with frame_lock:
        latest_frame = None          # Reset stale frame data
    with i3d_buffer_lock:
        i3d_frame_buffer.clear()

    background_tasks.add_task(
        process_stream,
        request.rtsp_url,
        request.location,
        request.user_id,
        request.camera_id,
        request.camera_name,
    )

    return {"message": "Ensemble detection started (Weapon + Activity + Face)"}


@app.post("/stop-detection")
async def stop_detection():
    global detection_active
    detection_active = False
    return {"message": "Detection stopped"}


@app.get("/health")
async def health():
    return {
        "status": "ok",
        "service": "AttackShield AI",
        "models": ["YOLOv10 (Weapon)", "I3D R3D-18 (Activity)", "RetinaFace (Face)"],
        "time": datetime.now().isoformat(),
    }


@app.get("/metrics")
async def get_metrics():
    """Return ensemble detection metrics for the dashboard."""
    with metrics_lock:
        return {
            "success": True,
            "data": {**ensemble_metrics},
        }


@app.get("/trust-score")
async def get_trust_score():
    """Return current trust score and governance indicators."""
    return {
        "success": True,
        "data": {**trust_score},
    }


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8000)
