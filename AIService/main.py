"""
AttackShield AI Service
Multi-Model Ensemble:
  1. YOLOv10  – Weapon Detection (knife, pistol, gun)
  2. I3D (Inflated 3D ConvNet) – Suspicious Activity / Action Recognition
  3. YuNet – Real-time Face Detection (5-point landmarks feed SFace recognition)
Aligns with the AttackShield AI project proposal.
"""

from fastapi import FastAPI, BackgroundTasks, Depends, Header, HTTPException
from pydantic import BaseModel, Field
import cv2
import numpy as np
from ultralytics import YOLO
import requests
import time
from datetime import datetime
import os
import threading
import queue
import subprocess
import shutil
import base64
import glob
import asyncio
from collections import deque

import torch
import torch.nn.functional as F
from torchvision.models.video import r3d_18, R3D_18_Weights
from torchvision import transforms
from fusion_engine import FusionRuleEngine
from model_management import ModelManagementPipeline

app = FastAPI(title="AttackShield AI Service")

BASE_DIR = os.path.dirname(__file__)
MODEL_PATH = os.path.abspath(os.path.join(BASE_DIR, '..', 'Yolov10', 'model.pt'))
WEAPON_VERIFIER_PATH = os.path.join(BASE_DIR, 'yolo11n.pt')
SFACE_MODEL_PATH = os.path.join(BASE_DIR, 'face_recognition_sface_2021dec.onnx')
YUNET_MODEL_PATH = os.path.join(BASE_DIR, 'face_detection_yunet_2023mar.onnx')
MODEL_REGISTRY_PATH = os.path.join(BASE_DIR, 'model_registry.json')

# Inference device. The MX350 has only 2 GB VRAM, so YOLO runs at half
# precision on GPU while I3D stays wherever it fits.
DEVICE = "cuda" if torch.cuda.is_available() else "cpu"

# Ultralytics device string for the weapon model. When the OpenVINO backend is
# active the model is already compiled for CPU, and passing a torch device would
# be ignored, so the plugin target is named explicitly.
WEAPON_DEVICE = DEVICE if DEVICE == "cuda" else "intel:cpu"

# Thread budget for a 4-physical-core CPU, chosen from measurement: with a
# competing face thread, YOLO ran 803/607/535/515 ms at 1/2/3/4 torch threads,
# so 3 keeps nearly all the speedup while leaving a core for the face and
# activity threads. OpenVINO manages its own pool; this bounds I3D and the verifier.
torch.set_num_threads(3)
cv2.setNumThreads(2)
BACKEND_URL = os.environ.get(
    'BACKEND_URL',
    "http://localhost:5217/api/detections/receive"
)
# Bound on the async backend dispatch queue. Detections are already deduplicated
# per type, so a backlog this deep means the backend is down; dropping the oldest
# entry is preferable to stalling inference.
BACKEND_QUEUE_MAX = 64
MODEL_ADMIN_TOKEN = os.environ.get('MODEL_ADMIN_TOKEN')
fusion_engine = FusionRuleEngine(alert_threshold=0.60)
model_pipeline = ModelManagementPipeline(MODEL_REGISTRY_PATH, BASE_DIR)


class RegisterModelRequest(BaseModel):
    name: str
    version: str
    path: str
    sha256: str | None = None


class ActivateModelRequest(BaseModel):
    name: str


def require_model_admin(x_model_admin_token: str | None = Header(default=None)):
    if not MODEL_ADMIN_TOKEN or x_model_admin_token != MODEL_ADMIN_TOKEN:
        raise HTTPException(status_code=403, detail="Model management is not configured or authorized")

# ── Pre-load all models when the service starts ────────────────────────
# Model loading (especially I3D weight download) can take 30-120 s.
# Running it at startup in a background thread means /start-detection
# returns in <100 ms instead of blocking until models are ready.
@app.on_event("startup")
async def preload_models():
    loop = asyncio.get_event_loop()
    print("[Startup] Pre-loading all AI models in background thread...")
    async def _load():
        defaults = [
            ("weapon-yolov10", "current", MODEL_PATH),
            ("knife-verifier", "yolo11n", WEAPON_VERIFIER_PATH),
            ("face-recognition", "sface-2021dec", SFACE_MODEL_PATH),
            ("face-detection", "yunet-2023mar", YUNET_MODEL_PATH),
        ]
        for name, version, path in defaults:
            if not any(item.name == name for item in model_pipeline.list()):
                model_pipeline.register(name, version, path)
        await loop.run_in_executor(None, load_yolo_model)
        await loop.run_in_executor(None, load_i3d_model)
        await loop.run_in_executor(None, load_face_detector)
        print("[Startup] ✅ All models loaded and ready!")
    asyncio.create_task(_load())

CONFIDENCE_THRESHOLD = 0.25            # Let the class-specific thresholds decide per weapon
KNIFE_CONFIDENCE_THRESHOLD = 0.45      # Low-resolution streams often score knives below 0.70
WEAPON_CONFIRMATION_FRAMES = 2         # Require repeated detections before alerting
WEAPON_CONFIRMATION_IOU = 0.20        # Tolerate webcam bounding-box jitter
# YOLOv10-L is a 25.8M-parameter network, so input size dominates its cost.
# Measured on an i5-1135G7 via the OpenVINO CPU backend: 320 px = 310 ms,
# 256 px = 300 ms, 192 px = 175 ms, 160 px = 97 ms. 256 px keeps enough
# resolution for a hand-held weapon while staying well inside the cadence.
WEAPON_INFERENCE_SIZE = 256
FACE_DETECTION_SIZE = 320              # YuNet input width; ~9 ms per frame at 320x240
FACE_SCORE_THRESHOLD = 0.6             # YuNet internal score gate for the live pass
ACTIVITY_DETECTION_INTERVAL = 8        # Run I3D every 8 frames (half-clip overlap)

# ── Hit-list (face re-identification) tuning ─────────────────────────
# SFace's cosine score for the same person typically lands around 0.363 on the
# LFW benchmark; OpenCV's own sample uses that as the accept threshold. The old
# 0.45 gate was well above it, so real matches on 640x480 CCTV-style frames were
# rejected and hit-list detection appeared to be broken. 0.36 restores the
# reference behaviour while staying above the impostor distribution.
HIT_LIST_MATCH_THRESHOLD = 0.36
# Reference photos are enrolled once and never re-checked, so accept a slightly
# weaker YuNet score than the live pass (0.6) - but not much weaker. Measured on
# a face-free photo, YuNet still emits ~0.27 blobs a few pixels across, so a very
# low gate would enrol noise as a "person" and poison every later comparison.
HIT_LIST_ENROLL_CONFIDENCE = 0.50
# SFace aligns to 112x112, so a reference face smaller than this yields a badly
# upscaled, near-useless embedding. It also rejects the tiny false positives that
# a relaxed score threshold lets through.
HIT_LIST_MIN_REFERENCE_FACE_PX = 40
# Consecutive confirmations before alerting. The face pass runs every 100 ms, so
# 2 confirmations is ~200 ms of agreement - enough to suppress a single-frame
# false positive without losing a person who only briefly faces the camera.
HIT_LIST_CONFIRMATION_FRAMES = 2

# Detector cadences (seconds). Each heavy model runs in its own thread at a
# bounded rate so the main loop keeps its ~30 ms budget instead of blocking.
# These are duty-cycle limits, not just refresh rates: I3D costs ~500 ms and
# the weapon pass ~300 ms, so the periods sit above those costs to stop the
# detectors from saturating all 4 physical cores and starving each other.
# Leaving idle headroom is what keeps per-pass latency near its solo cost.
WEAPON_DETECTION_PERIOD_SECONDS = 0.50
FACE_DETECTION_PERIOD_SECONDS = 0.10
ACTIVITY_DETECTION_PERIOD_SECONDS = 2.00
# Ceiling on the main loop's own rate (~30 fps). Bounds the loop when a VOD or
# HLS source delivers frames faster than real time.
MAIN_LOOP_MIN_PERIOD_SECONDS = 1.0 / 30.0

# Per-type deduplication windows (seconds)
DUPLICATE_WINDOWS = {
    "weapon":             10,   # Resend weapon alert every 10 s
    "suspicious_activity": 30,  # Resend activity alert every 30 s
    "face":               3,    # Resend face bbox every 3 s (keeps overlay alive)
    "hit_list":          60,
}
I3D_CLIP_LENGTH = 16                   # Number of frames per I3D clip
I3D_CONFIDENCE_THRESHOLD = 0.35        # Avoid low-confidence action false positives
# Buffer clip frames at the size R3D-18's own transform resizes to (W, H), so
# the 16-frame buffer holds ~1 MB instead of ~14 MB without changing the crop.
I3D_BUFFER_SIZE = (171, 128)
# R3D-18 is exported to OpenVINO IR once, then cached on disk. Measured at
# 1x3x16x112x112: torch CPU 971 ms, OpenVINO CPU 225 ms, OpenVINO iGPU 167 ms.
# The iGPU is tried first because it also frees CPU cores for the weapon pass.
I3D_OPENVINO_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "r3d18_openvino")
I3D_OPENVINO_XML = os.path.join(I3D_OPENVINO_DIR, "r3d18.xml")
I3D_OPENVINO_DEVICE_ORDER = ("GPU.0", "GPU", "CPU")
# Returned by detect_suspicious_activity_i3d when there is no new clip to run,
# so the worker can tell "no inference happened" from "inference found nothing"
# and keep skipped passes out of the latency metric.
SKIPPED_CLIP: list = []


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
    # Physical violence
    "punching person",
    "wrestl",         # "wrestling"
    "headbutt",       # "headbutting"
    "slap",           # "slapping"
    "sword",          # "sword fighting"
    "drop kick",      # "drop kicking"
    # Weapon use
    "archery",
    "throwing axe",
    # Vandalism signals
    "spray paint",
    "graffiti",
    "playing paintball",
    "dragging",
]

# Pre-compute lowercase keywords once at import time (minor speed gain).
_SUSPICIOUS_KW_LOWER = [kw.lower() for kw in SUSPICIOUS_KEYWORDS]

# ── Models ──────────────────────────────────────────────────────────
yolo_model = None
weapon_backend = DEVICE   # reported by /health; "openvino:cpu" once exported
weapon_verifier_model = None
i3d_model = None
i3d_weights = None
i3d_categories = None
i3d_preprocess = None
i3d_ov_model = None       # OpenVINO compiled R3D-18 (preferred when available)
i3d_backend = "torch:cpu" # reported by /health
i3d_lock = threading.Lock()   # OpenVINO InferRequest is not thread-safe
face_detector = None
face_detector_lock = threading.Lock()   # cv2 FaceDetectorYN keeps internal state
sface_model = None
model_errors = {}
class DetectionSession:
    """Mutable state owned by one camera detection worker."""

    def __init__(self, camera_id, location, user_id, camera_name, hit_list=None):
        self.camera_id = camera_id
        self.location = location
        self.user_id = user_id
        self.camera_name = camera_name
        self.stop_event = threading.Event()
        self.frame_lock = threading.Lock()
        self.latest_frame = None
        self.latest_frame_sequence = 0
        self.i3d_buffer_lock = threading.Lock()
        self.i3d_frame_buffer = deque(maxlen=I3D_CLIP_LENGTH)
        self.i3d_frame_count = 0
        self.i3d_last_processed_count = 0
        self.face_tracker = FaceTracker(iou_threshold=0.25, max_age=30)
        self.hit_list = hit_list or []
        self.hit_list_embeddings = []
        self.hit_list_confirmations = {}
        self.weapon_confirmations = {}
        self.last_detections = {}
        self.frame_count = 0
        self.last_frame_at = None
        self.stream_error = None

    @property
    def active(self):
        return not self.stop_event.is_set()


sessions = {}
sessions_lock = threading.Lock()

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
    "avg_inference_latency_ms": 0.0,   # main loop: frame intake -> ensemble
    "avg_weapon_latency_ms": 0.0,      # YOLO pass, measured in its worker
    "avg_face_latency_ms": 0.0,        # YuNet pass, measured in its worker
    "avg_activity_latency_ms": 0.0,    # I3D clip pass, measured in its worker
    "ensemble_confidence": 0.0,
}
metrics_lock = threading.Lock()


class DetectionRequest(BaseModel):
    rtsp_url: str
    location: str
    user_id: str | None = None
    camera_id: str | None = None
    camera_name: str | None = None
    hit_list: list[dict] = Field(default_factory=list)


class StopDetectionRequest(BaseModel):
    camera_id: str | None = None


# ═══════════════════════════════════════════════════════════════════
# Model Loaders
# ═══════════════════════════════════════════════════════════════════

def _openvino_weapon_model():
    """Return an OpenVINO-optimised weapon model, exporting it on first use.

    OpenVINO's CPU plugin runs this network materially faster than torch-CPU
    (measured ~310 ms vs ~514 ms at imgsz=320 on an i5-1135G7), and the export
    is cached on disk so the cost is paid once. Returns None if anything fails
    so the caller can fall back to the torch weights.
    """
    export_dir = os.path.splitext(MODEL_PATH)[0] + "_openvino_model"
    try:
        if not os.path.isdir(export_dir):
            print(f"[YOLO] Exporting weapon model to OpenVINO (imgsz={WEAPON_INFERENCE_SIZE})...")
            export_dir = YOLO(MODEL_PATH).export(
                format="openvino", imgsz=WEAPON_INFERENCE_SIZE, half=True, dynamic=False
            )
        model = YOLO(export_dir, task="detect")
        print(f"[YOLO] Using OpenVINO weapon backend: {export_dir}")
        return model
    except Exception as exc:
        print(f"[YOLO] OpenVINO unavailable, falling back to torch: {exc}")
        return None


def load_yolo_model():
    """Load YOLOv10 weapon detection model."""
    global yolo_model, weapon_verifier_model, weapon_backend
    if yolo_model is not None and weapon_verifier_model is not None:
        return
    try:
        print(f"[YOLO] Loading weapon model from {MODEL_PATH}")
        if DEVICE == "cpu":
            yolo_model = _openvino_weapon_model()
        if yolo_model is None:
            yolo_model = YOLO(MODEL_PATH)
            weapon_backend = DEVICE
        else:
            weapon_backend = "openvino:cpu"
        # The verifier stays on torch: it is a 5 MB nano model whose cost is
        # already small, so a second export would add startup time for nothing.
        weapon_verifier_model = YOLO(WEAPON_VERIFIER_PATH)
        # Warm up once so the first real frame does not pay allocation cost.
        warmup = np.zeros((GRAB_H, GRAB_W, 3), dtype=np.uint8)
        yolo_model(warmup, imgsz=WEAPON_INFERENCE_SIZE, device=WEAPON_DEVICE,
                   half=(DEVICE == "cuda"), verbose=False)
        weapon_verifier_model(warmup, imgsz=WEAPON_INFERENCE_SIZE, device=DEVICE,
                              half=(DEVICE == "cuda"), verbose=False)
        model_errors.pop("yolo", None)
        print(f"[YOLO] Weapon model loaded on {weapon_backend}")
    except Exception as exc:
        model_errors["yolo"] = str(exc)
        raise


def _openvino_i3d_model(torch_model):
    """
    Compile R3D-18 with OpenVINO, preferring the integrated GPU.

    Measured on this machine (i5-1135G7 + Iris Xe), 1x3x16x112x112:
        torch CPU (3 threads) : 971 ms
        OpenVINO CPU          : 225 ms
        OpenVINO iGPU         : 167 ms

    The iGPU is the important win: it is 5.8x faster *and* it takes the clip
    classifier off the CPU entirely, so it stops stealing cores from the
    latency-critical weapon detector.
    """
    try:
        import openvino as ov
    except ImportError:
        print("[I3D] OpenVINO not installed - falling back to torch CPU")
        return None, None

    example = torch.zeros(1, 3, I3D_CLIP_LENGTH, 112, 112)
    try:
        core = ov.Core()
        if os.path.isdir(I3D_OPENVINO_DIR) and os.path.isfile(I3D_OPENVINO_XML):
            model = core.read_model(I3D_OPENVINO_XML)
        else:
            print("[I3D] Converting R3D-18 to OpenVINO IR (one-time)...")
            model = ov.convert_model(torch_model, example_input=example)
            os.makedirs(I3D_OPENVINO_DIR, exist_ok=True)
            ov.save_model(model, I3D_OPENVINO_XML)

        available = core.available_devices
        for device in I3D_OPENVINO_DEVICE_ORDER:
            if device not in available:
                continue
            try:
                compiled = core.compile_model(model, device)
                compiled(example.numpy())    # warm up / validate
                return compiled, device
            except Exception as exc:
                print(f"[I3D] OpenVINO {device} unavailable ({str(exc)[:80]})")
        print("[I3D] No usable OpenVINO device - falling back to torch CPU")
        return None, None
    except Exception as exc:
        print(f"[I3D] OpenVINO setup failed ({str(exc)[:120]}) - using torch CPU")
        return None, None


def load_i3d_model():
    """
    Load I3D (Inflated 3D ConvNet) for action recognition.
    Uses torchvision's R3D-18 pretrained on Kinetics-400.
    R3D-18 is an I3D-family model (3D ResNet with inflated convolutions).
    """
    global i3d_model, i3d_weights, i3d_categories, i3d_preprocess
    global i3d_ov_model, i3d_backend

    if i3d_model is not None:
        return
    try:
        print("[I3D] Loading action recognition model (R3D-18, Kinetics-400)...")
        i3d_weights = R3D_18_Weights.KINETICS400_V1
        i3d_model = r3d_18(weights=i3d_weights)
        i3d_model.eval()

        i3d_categories = i3d_weights.meta["categories"]
        i3d_preprocess = i3d_weights.transforms()

        # Prefer OpenVINO on the iGPU: 167 ms vs 971 ms on torch CPU, and it
        # frees CPU cores for the weapon detector. Falls back to torch CPU.
        i3d_ov_model, ov_device = _openvino_i3d_model(i3d_model)
        if i3d_ov_model is not None:
            i3d_backend = f"openvino:{ov_device.lower()}"
        else:
            i3d_backend = "torch:cpu"
        print(f"   I3D backend: {i3d_backend}")

        model_errors.pop("i3d", None)
        print(f"[I3D] Model loaded - {len(i3d_categories)} action categories")
    except Exception as exc:
        model_errors["i3d"] = str(exc)
        raise


def load_face_detector():
    """
    YuNet face detector (OpenCV DNN).
    Replaces RetinaFace, which cost ~2.7 s per frame on CPU and saturated every
    core through TensorFlow. YuNet runs in ~9 ms and emits the same 5 landmarks
    that SFace alignment requires.
    """
    global face_detector
    if face_detector is not None:
        return
    try:
        print("[YuNet] Initializing face detector...")
        if not os.path.exists(YUNET_MODEL_PATH):
            raise FileNotFoundError(
                f"YuNet model missing at {YUNET_MODEL_PATH}. Download "
                "face_detection_yunet_2023mar.onnx from the OpenCV Zoo."
            )
        face_detector = cv2.FaceDetectorYN_create(
            YUNET_MODEL_PATH,
            "",
            (FACE_DETECTION_SIZE, FACE_DETECTION_SIZE),
            FACE_SCORE_THRESHOLD,
            0.3,    # NMS threshold
            5000,   # top-k
        )
        model_errors.pop("face_detector", None)
        print("[YuNet] Model loaded")
    except Exception as exc:
        model_errors["face_detector"] = str(exc)
        raise


# ═══════════════════════════════════════════════════════════════════
# Detection Functions (Ensemble Components)
# ═══════════════════════════════════════════════════════════════════

def _coco_knife_boxes(frame):
    """Run the COCO knife verifier once and return every knife box it found.

    Callers must reuse the result for all candidates in a frame: the verifier is
    a full-frame YOLO pass (~150 ms on CPU) whose output does not depend on the
    candidate, so invoking it per candidate multiplied the cost for nothing.
    """
    results = weapon_verifier_model(
        frame,
        conf=0.25,
        classes=[43],
        imgsz=WEAPON_INFERENCE_SIZE,
        device=DEVICE,
        half=(DEVICE == "cuda"),
        verbose=False,
    )
    frame_height, frame_width = frame.shape[:2]
    boxes = []
    for result in results:
        if not hasattr(result, "boxes") or result.boxes is None:
            continue
        for box in result.boxes:
            x1, y1, x2, y2 = [float(value) for value in box.xyxy[0]]
            boxes.append({
                "x": x1 / frame_width,
                "y": y1 / frame_height,
                "w": (x2 - x1) / frame_width,
                "h": (y2 - y1) / frame_height,
            })
    return boxes


def _confirm_weapon_across_frames(session, detections):
    confirmed = []
    next_confirmations = {}
    for detection in detections:
        label = detection["label"].lower()
        previous = session.weapon_confirmations.get(label)
        count = 1
        if previous and FaceTracker._iou(previous["bbox"], detection["bbox"]) >= WEAPON_CONFIRMATION_IOU:
            count = previous["count"] + 1
        next_confirmations[label] = {"bbox": detection["bbox"], "count": count}
        if count >= WEAPON_CONFIRMATION_FRAMES:
            confirmed.append(detection)
    session.weapon_confirmations = next_confirmations
    return confirmed


def detect_weapons(frame, session):
    """Detect weapons using class-specific, ensemble, and temporal confirmation."""
    results = yolo_model(
        frame,
        conf=CONFIDENCE_THRESHOLD,
        classes=[0, 1],
        imgsz=WEAPON_INFERENCE_SIZE,
        device=WEAPON_DEVICE,
        half=(DEVICE == "cuda"),
        verbose=False,
    )
    detections = []
    knife_detections = []
    fh, fw = frame.shape[:2]

    for result in results:
        if not hasattr(result, 'boxes') or result.boxes is None:
            continue
        for box in result.boxes:
            cls  = int(box.cls)
            conf = float(box.conf)
            if conf < CONFIDENCE_THRESHOLD:
                continue
            class_name = yolo_model.names[cls]
            normalized_name = class_name.lower().strip()
            if normalized_name not in {"knife", "pistol"}:
                continue
            if normalized_name == "knife" and conf < KNIFE_CONFIDENCE_THRESHOLD:
                continue
            display_label = class_name

            x1, y1, x2, y2 = [float(v) for v in box.xyxy[0]]
            detection = {
                "type": "weapon",
                "label": display_label,
                "confidence": conf,
                "bbox": {
                    "x": round(x1 / fw, 4),
                    "y": round(y1 / fh, 4),
                    "w": round((x2 - x1) / fw, 4),
                    "h": round((y2 - y1) / fh, 4),
                },
            }
            if normalized_name == "knife":
                knife_detections.append(detection)
            detections.append(detection)

    if knife_detections:
        # COCO's generic knife detector is useful corroboration, but it is
        # less reliable than the weapon model on small or partially visible knives.
        # One verifier pass covers every knife candidate in this frame.
        verifier_boxes = _coco_knife_boxes(frame)
        for detection in knife_detections:
            corroborated = any(
                FaceTracker._iou(detection["bbox"], verifier_bbox) >= WEAPON_CONFIRMATION_IOU
                for verifier_bbox in verifier_boxes
            )
            if corroborated:
                detection["model"] = "YOLOv10 + COCO knife verifier"
            else:
                detection["model"] = "YOLOv10"
                print(f"[Weapon] Knife candidate accepted without verifier ({detection['confidence']:.0%})")

    confirmed = _confirm_weapon_across_frames(session, detections)
    for detection in confirmed:
        print(f"[Weapon] Confirmed: {detection['label']} ({detection['confidence']:.0%})")
    return confirmed


def detect_suspicious_activity_i3d(session):
    """
    I3D-based suspicious activity detection using R3D-18 (Inflated 3D ConvNet).
    Classifies a clip of 16 frames into one of 400 Kinetics action classes.
    If the top prediction is in the SUSPICIOUS_ACTIONS set, we flag it.
    """
    detections = []

    with session.i3d_buffer_lock:
        if len(session.i3d_frame_buffer) < I3D_CLIP_LENGTH:
            return SKIPPED_CLIP
        if session.i3d_frame_count - session.i3d_last_processed_count < ACTIVITY_DETECTION_INTERVAL:
            return SKIPPED_CLIP
        clip_frames = list(session.i3d_frame_buffer)
        session.i3d_last_processed_count = session.i3d_frame_count

    # Prepare clip: resize → uint8 tensor → (T, C, H, W) → preprocess
    # IMPORTANT: R3D_18_Weights.transforms() (VideoClassification) expects
    # uint8 tensors in range [0, 255] shaped (T, C, H, W). Do NOT divide by
    # 255 here – the transform handles normalisation internally.
    processed = []
    for frame in clip_frames:
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        resized = cv2.resize(rgb, (112, 112))
        # Keep as uint8 – the transform expects [0, 255]
        tensor = torch.from_numpy(resized).permute(2, 0, 1)   # (C, H, W) uint8
        processed.append(tensor)

    # Stack to (T, C, H, W); the transform returns (C, T, H, W).
    clip_tensor = torch.stack(processed, dim=0)          # (T, C, H, W) uint8

    # Apply the pretrained transforms (resize + crop + normalize to float)
    clip_tensor = i3d_preprocess(clip_tensor)             # (C, T, H, W) float

    # Add batch dimension.
    batch = clip_tensor.unsqueeze(0)                      # (1, C, T, H, W)

    if i3d_ov_model is not None:
        # OpenVINO InferRequest state is not thread-safe; only one clip is in
        # flight at a time anyway (single activity worker).
        with i3d_lock:
            logits = torch.from_numpy(i3d_ov_model(batch.numpy())[0])
    else:
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


def detect_faces(frame, tracker):
    """
    YuNet face detection.
    Returns normalized bounding boxes, confidence and the 5 landmarks SFace
    needs. Frame is downscaled before inference, then coordinates are
    normalized so callers stay resolution-independent.
    """
    detections = []
    if face_detector is None:
        return detections

    fh, fw = frame.shape[:2]
    scale = min(1.0, FACE_DETECTION_SIZE / fw)
    if scale < 1.0:
        small = cv2.resize(frame, (int(fw * scale), int(fh * scale)))
    else:
        small = frame
    sh, sw = small.shape[:2]

    try:
        with face_detector_lock:
            face_detector.setInputSize((sw, sh))
            _, faces = face_detector.detect(small)
    except Exception as e:
        print(f"[YuNet] Error: {e}")
        return detections

    if faces is None:
        return detections

    # YuNet row layout: x, y, w, h, then 5 landmark x/y pairs, then score.
    landmark_names = ["right_eye", "left_eye", "nose", "mouth_right", "mouth_left"]

    for face in faces:
        conf = float(face[14])
        if conf < 0.40:   # Match backend face threshold
            continue

        x, y, w, h = (float(v) for v in face[:4])
        landmarks = {
            name: [round(float(face[4 + i * 2]) / scale), round(float(face[5 + i * 2]) / scale)]
            for i, name in enumerate(landmark_names)
        }
        detections.append({
            "type": "face",
            "label": "Person",       # will be overwritten by tracker below
            "confidence": round(conf, 3),
            "bbox": {
                "x": round(x / sw, 4),
                "y": round(y / sh, 4),
                "w": round(w / sw, 4),
                "h": round(h / sh, 4),
            },
            "model": "YuNet",
            "landmarks": landmarks,
        })

    # Assign persistent person IDs (Person 1, Person 2, …)
    return tracker.update(detections)


def _load_sface():
    global sface_model
    if sface_model is None:
        sface_model = cv2.FaceRecognizerSF_create(SFACE_MODEL_PATH, "")
    return sface_model


def _decode_data_image(image_url):
    encoded = image_url.split(",", 1)[1]
    return cv2.imdecode(np.frombuffer(base64.b64decode(encoded), np.uint8), cv2.IMREAD_COLOR)


def _face_embedding(frame, detection):
    bbox = detection["bbox"]
    landmarks = detection.get("landmarks", {})
    required = ["right_eye", "left_eye", "nose", "mouth_right", "mouth_left"]
    if not all(key in landmarks for key in required):
        return None
    height, width = frame.shape[:2]
    face_box = np.array([
        bbox["x"] * width,
        bbox["y"] * height,
        bbox["w"] * width,
        bbox["h"] * height,
        *landmarks["right_eye"],
        *landmarks["left_eye"],
        *landmarks["nose"],
        *landmarks["mouth_right"],
        *landmarks["mouth_left"],
    ], dtype=np.float32)
    recognizer = _load_sface()
    return recognizer.feature(recognizer.alignCrop(frame, face_box))


def prepare_hit_list(session):
    # The reference photos are enrolled with YuNet, so the detector must already
    # be loaded. prepare_hit_list used to run before the lazy load in some paths,
    # and detect_faces silently returns [] when face_detector is None, which made
    # every enrolment fail with no usable error.
    if face_detector is None:
        try:
            load_face_detector()
        except Exception as exc:
            print(f"[HitList] Face detector unavailable, cannot enrol references: {exc}")
            return

    for entry in session.hit_list:
        try:
            reference = _decode_data_image(entry["image_url"])
            if reference is None:
                print(f"[HitList] Skipping {entry.get('name')}: reference image could not be decoded")
                continue

            faces = detect_faces_for_enrollment(reference)
            if not faces:
                print(f"[HitList] Skipping {entry.get('name')}: no usable face in the reference photo "
                      f"(need a clear, front-facing, well-lit face at least "
                      f"{HIT_LIST_MIN_REFERENCE_FACE_PX}px wide)")
                continue
            if len(faces) > 1:
                # Enrol the largest face rather than rejecting the photo: group
                # shots and busy backgrounds are common and previously discarded
                # the entry entirely.
                faces.sort(key=lambda f: f["bbox"]["w"] * f["bbox"]["h"], reverse=True)
                print(f"[HitList] {entry.get('name')}: {len(faces)} faces in reference, "
                      f"enrolling the largest")

            embedding = _face_embedding(reference, faces[0])
            if embedding is None:
                print(f"[HitList] Skipping {entry.get('name')}: face landmarks incomplete")
                continue
            session.hit_list_embeddings.append((entry, embedding))
            print(f"[HitList] Enrolled {entry.get('name')}")
        except Exception as exc:
            print(f"[HitList] Could not prepare {entry.get('name')}: {exc}")

    if session.hit_list_embeddings:
        print(f"[HitList] Prepared {len(session.hit_list_embeddings)} reference face(s) "
              f"of {len(session.hit_list)} entry(ies)")
    else:
        print(f"[HitList] No usable reference faces from {len(session.hit_list)} entry(ies) - "
              f"hit-list matching is disabled for this session")


def detect_faces_for_enrollment(reference):
    """
    Detect faces in a still hit-list reference photo.

    Live detection downscales to 320 px for speed and YuNet is constructed with a
    0.6 internal score threshold, both of which are wrong for enrolment: a missed
    reference silently disables matching for that person forever. This runs at
    full resolution with a relaxed score threshold and retries upscaled when
    nothing is found. Coordinates are returned in the original image's space so
    _face_embedding can align against `reference`.
    """
    if face_detector is None:
        return []

    height, width = reference.shape[:2]
    landmark_names = ["right_eye", "left_eye", "nose", "mouth_right", "mouth_left"]
    # Small crops (e.g. a 120 px thumbnail) fall below YuNet's practical minimum
    # face size, so retry at 2x if the first pass finds nothing.
    scales = [1.0, 2.0] if max(height, width) < 640 else [1.0]

    # The detector is shared with the live face worker, so the threshold override
    # stays inside the lock and is always restored.
    with face_detector_lock:
        try:
            face_detector.setScoreThreshold(HIT_LIST_ENROLL_CONFIDENCE)
            for scale in scales:
                if scale == 1.0:
                    candidate = reference
                else:
                    candidate = cv2.resize(reference, (int(width * scale), int(height * scale)),
                                           interpolation=cv2.INTER_CUBIC)
                ch, cw = candidate.shape[:2]
                try:
                    face_detector.setInputSize((cw, ch))
                    _, faces = face_detector.detect(candidate)
                except Exception as e:
                    print(f"[HitList] YuNet error during enrolment: {e}")
                    return []

                if faces is None:
                    continue

                found = []
                for face in faces:
                    x, y, w, h = (float(v) for v in face[:4])
                    # Reject sub-40 px blobs: YuNet emits low-confidence noise
                    # detections a few pixels across even on face-free photos,
                    # and SFace cannot build a usable embedding from them. The
                    # size is measured in `reference` space, so undo any upscale.
                    if min(w, h) / scale < HIT_LIST_MIN_REFERENCE_FACE_PX:
                        continue
                    found.append({
                        "type": "face",
                        "label": "Reference",
                        "confidence": round(float(face[14]), 3),
                        "bbox": {
                            "x": round(x / cw, 4),
                            "y": round(y / ch, 4),
                            "w": round(w / cw, 4),
                            "h": round(h / ch, 4),
                        },
                        "model": "YuNet",
                        # Landmarks are absolute pixels, so undo the upscale to
                        # land back in `reference` space.
                        "landmarks": {
                            name: [round(float(face[4 + i * 2]) / scale),
                                   round(float(face[5 + i * 2]) / scale)]
                            for i, name in enumerate(landmark_names)
                        },
                    })
                if found:
                    return found
            return []
        finally:
            face_detector.setScoreThreshold(FACE_SCORE_THRESHOLD)
            face_detector.setInputSize((FACE_DETECTION_SIZE, FACE_DETECTION_SIZE))


def match_hit_list(frame, face_detections, session):
    matches = []
    seen = set()
    if not session.hit_list_embeddings:
        return matches
    recognizer = _load_sface()
    for face in face_detections:
        if face.get("type") != "face":
            continue
        embedding = _face_embedding(frame, face)
        if embedding is None:
            continue
        best_entry, best_score = None, 0.0
        for entry, reference_embedding in session.hit_list_embeddings:
            score = float(recognizer.match(reference_embedding, embedding, cv2.FaceRecognizerSF_FR_COSINE))
            if score > best_score:
                best_entry, best_score = entry, score
        if best_entry is None:
            continue
        if best_score < HIT_LIST_MATCH_THRESHOLD:
            # Logged so the threshold can be tuned against real footage instead
            # of guessed at: silent rejection was the main reason hit-list
            # matching appeared to do nothing.
            print(f"[HitList] Best candidate {best_entry['name']} scored "
                  f"{best_score:.3f} < {HIT_LIST_MATCH_THRESHOLD:.3f} - rejected")
            continue
        entry_id = best_entry["id"]
        seen.add(entry_id)
        session.hit_list_confirmations[entry_id] = session.hit_list_confirmations.get(entry_id, 0) + 1
        confirmations = session.hit_list_confirmations[entry_id]
        if confirmations < HIT_LIST_CONFIRMATION_FRAMES:
            print(f"[HitList] {best_entry['name']} match {best_score:.3f} "
                  f"({confirmations}/{HIT_LIST_CONFIRMATION_FRAMES} confirmations)")
            continue
        print(f"[HitList] MATCH: {best_entry['name']} ({best_score:.3f})")
        matches.append({
            "type": "hit_list",
            "label": best_entry["name"],
            "confidence": round(best_score, 3),
            "bbox": face["bbox"],
            "priority": "high",
            "threat_level": "critical",
        })
    for entry_id in list(session.hit_list_confirmations):
        if entry_id not in seen:
            session.hit_list_confirmations[entry_id] = 0
    return matches


# ═══════════════════════════════════════════════════════════════════
# Ensemble Decision Engine
# ═══════════════════════════════════════════════════════════════════

def ensemble_decision(weapon_dets, activity_dets, face_dets):
    """
    Aggregate detections from all models and compute ensemble confidence.
    Threat alerts are generated when ensemble confidence exceeds a threshold.
    """
    decision = fusion_engine.evaluate(weapon_dets, activity_dets, face_dets)
    return decision.detections, decision.confidence


# ═══════════════════════════════════════════════════════════════════
# Backend Communication
# ═══════════════════════════════════════════════════════════════════

def _build_detection_screenshot(frame, detection):
    screenshot = frame.copy()
    bbox = detection.get("bbox")
    if bbox:
        height, width = screenshot.shape[:2]
        x1 = int(bbox["x"] * width)
        y1 = int(bbox["y"] * height)
        x2 = int((bbox["x"] + bbox["w"]) * width)
        y2 = int((bbox["y"] + bbox["h"]) * height)
        cv2.rectangle(screenshot, (x1, y1), (x2, y2), (0, 0, 255), 3)

    encoded, buffer = cv2.imencode(".jpg", screenshot, [cv2.IMWRITE_JPEG_QUALITY, 80])
    if not encoded:
        return None
    return "data:image/jpeg;base64," + base64.b64encode(buffer).decode("ascii")


def send_detection_to_backend(detection, location, confidence, frame=None, user_id=None, camera_id=None, camera_name=None):
    det_type = detection.get("type", "weapon")
    label = detection.get("label", "Unknown")
    image_url = (
        _build_detection_screenshot(frame, detection)
        if frame is not None and det_type in {"weapon", "suspicious_activity", "hit_list"}
        else None
    )

    payload = {
        "weaponType": label,
        "detectionType": det_type,       # weapon | suspicious_activity | face
        "location": location,
        "confidence": confidence,
        "imageUrl": image_url,
        "userId": user_id,
        "cameraId": camera_id,
        "cameraName": camera_name,
        "bbox": detection.get("bbox"),   # normalized 0-1 bbox (if available)
        "priority": detection.get("priority", "medium"),
        "threatLevel": detection.get("threat_level", "warning"),
        "ensembleModel": True,
    }

    _enqueue_backend_post(payload, det_type, label, confidence)


# ── Asynchronous backend dispatch ──────────────────────────────────
# The POST used to run inline in the detection loop, so a slow or unreachable
# backend stalled inference for up to the full 5 s HTTP timeout on every
# detection. Detections are now handed to a dedicated sender thread; the
# detection loop never blocks on network I/O.
_backend_queue: "queue.Queue[dict]" = queue.Queue(maxsize=BACKEND_QUEUE_MAX)
_backend_session = requests.Session()
_backend_session.mount(
    "http://",
    requests.adapters.HTTPAdapter(pool_connections=4, pool_maxsize=4, max_retries=0),
)
_backend_sender_started = False
_backend_sender_lock = threading.Lock()


def _backend_sender_loop():
    while True:
        item = _backend_queue.get()
        try:
            response = _backend_session.post(BACKEND_URL, json=item["payload"], timeout=5)
            if response.status_code == 200:
                print(f"[Detection] Sent: [{item['type']}] {item['label']} "
                      f"({item['confidence']:.0%})")
            else:
                print("[Detection] Backend error:", response.text)
        except Exception as e:
            print("[Detection] Request failed:", e)
        finally:
            _backend_queue.task_done()


def _ensure_backend_sender():
    """Start the sender thread once, on first use."""
    global _backend_sender_started
    if _backend_sender_started:
        return
    with _backend_sender_lock:
        if _backend_sender_started:
            return
        threading.Thread(target=_backend_sender_loop, daemon=True).start()
        _backend_sender_started = True


def _enqueue_backend_post(payload, det_type, label, confidence):
    _ensure_backend_sender()
    try:
        _backend_queue.put_nowait({
            "payload": payload,
            "type": det_type,
            "label": label,
            "confidence": confidence,
        })
    except queue.Full:
        # Backend is not keeping up. Dropping the oldest keeps the most recent
        # (most relevant) detections flowing instead of blocking inference.
        try:
            _backend_queue.get_nowait()
            _backend_queue.task_done()
            _backend_queue.put_nowait({
                "payload": payload,
                "type": det_type,
                "label": label,
                "confidence": confidence,
            })
            print("[Detection] Backend queue full - dropped oldest detection")
        except queue.Empty:
            pass


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

    is_hls = url.lower().split("?", 1)[0].endswith(".m3u8")

    cmd = [FFMPEG_BIN,
           "-loglevel", "error",          # suppress noisy decoder warnings
           # ── input flags ──────────────────────────────────────────
           "-fflags", "+genpts+nobuffer+discardcorrupt",
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
        "-flags", "low_delay",
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
        stderr=subprocess.DEVNULL,
        bufsize=0,
    )
    frame_size = GRAB_W * GRAB_H * 3   # bytes per raw BGR frame
    return proc, frame_size


def _read_raw_frame(stdout, frame_size: int):
    """Read one complete raw frame from a pipe that may return partial chunks."""
    chunks = bytearray()
    while len(chunks) < frame_size:
        chunk = stdout.read(frame_size - len(chunks))
        if not chunk:
            return None
        chunks.extend(chunk)
    return bytes(chunks)


def frame_grabber(rtsp_url, session):

    print(f"[Stream] Opening stream: {rtsp_url}")

    # ── Choose strategy based on URL type ────────────────────────────
    # HTTP MJPEG multipart streams use OpenCV. HLS playlists and RTSP streams
    # use FFmpeg because OpenCV does not reliably decode rolling HLS segments.
    is_mjpeg = (
        rtsp_url.startswith("http://") or rtsp_url.startswith("https://")
    ) and not rtsp_url.lower().split("?", 1)[0].endswith(".m3u8")

    if is_mjpeg:
        _frame_grabber_mjpeg(rtsp_url, session)
    else:
        _frame_grabber_ffmpeg(rtsp_url, session)

def _frame_grabber_mjpeg(url, session):
    """OpenCV VideoCapture-based grabber for HTTP MJPEG multipart streams."""

    max_retries = 8
    retry_delay = 2.0
    cap = None

    for attempt in range(1, max_retries + 1):
        if not session.active:
            return
        cap = cv2.VideoCapture(url)
        if cap.isOpened():
            # Confirm at least one frame is actually readable
            ret, test_frame = cap.read()
            if ret and test_frame is not None:
                with session.frame_lock:
                    session.latest_frame = test_frame
                    session.latest_frame_sequence += 1
                break
            cap.release()
            cap = None
        print(f"[Stream] MJPEG not ready (attempt {attempt}/{max_retries}), "
              f"retrying in {retry_delay:.0f}s…")
        time.sleep(retry_delay)
        retry_delay = min(retry_delay * 1.3, 8)

    if cap is None:
        print("[Stream] Unable to open MJPEG stream after all retries")
        session.stream_error = "Unable to open MJPEG stream after retries"
        session.stop_event.set()
        return

    print("[Stream] Frame grabber started (MJPEG/OpenCV)")

    consecutive_failures = 0
    while session.active:
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
                    session.stream_error = "MJPEG reconnect failed"
                    session.stop_event.set()
                    break
                consecutive_failures = 0
                print("[Stream] Reconnected to MJPEG stream")
            continue

        consecutive_failures = 0
        with session.frame_lock:
            session.latest_frame = frame
            session.latest_frame_sequence += 1
        session.last_frame_at = datetime.now().isoformat()

    cap.release()
    print("[Stream] Frame grabber stopped")


def _frame_grabber_ffmpeg(rtsp_url, session):
    """FFmpeg-pipe grabber for HLS and RTSP streams."""

    print(f"[Stream] Opening RTSP stream via FFmpeg pipe: {rtsp_url}")

    max_retries = 5
    retry_delay = 2.0
    proc = None
    frame_size = GRAB_W * GRAB_H * 3

    for attempt in range(1, max_retries + 1):
        if not session.active:
            return
        try:
            proc, frame_size = _open_ffmpeg_pipe(rtsp_url)
            test = _read_raw_frame(proc.stdout, frame_size)
            if test is not None:
                first = np.frombuffer(test, dtype=np.uint8).reshape(GRAB_H, GRAB_W, 3)
                with session.frame_lock:
                    session.latest_frame = first.copy()
                    session.latest_frame_sequence += 1
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
        session.stream_error = "Unable to open HLS/RTSP stream after retries"
        session.stop_event.set()
        return

    print("[Stream] Frame grabber started (RTSP/FFmpeg pipe)")

    consecutive_failures = 0
    while session.active:
        try:
            raw = _read_raw_frame(proc.stdout, frame_size)
        except Exception:
            break

        if raw is None:
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
                    session.stream_error = "HLS/RTSP reconnect failed"
                    session.stop_event.set()
                    break
            continue

        consecutive_failures = 0
        frame = np.frombuffer(raw, dtype=np.uint8).reshape(GRAB_H, GRAB_W, 3)
        with session.frame_lock:
            session.latest_frame = frame.copy()
            session.latest_frame_sequence += 1
        session.last_frame_at = datetime.now().isoformat()

    if proc:
        proc.kill()
    print("[Stream] Frame grabber stopped")


def process_stream(session, rtsp_url):
    """
    Main ensemble detection loop.
    - Weapon detection (YOLOv10) runs on every frame – maximum speed.
    - Face and activity detection run in dedicated background threads so
      they never block or slow down the weapon detection loop.
    """
    print("[AttackShield AI] Ensemble detection started")
    if session.hit_list:
        prepare_hit_list(session)

    grabber_thread = threading.Thread(
        target=frame_grabber,
        args=(rtsp_url, session),
        daemon=True,
    )
    grabber_thread.start()

    # ── Shared result lists for background detectors ──
    pending_face_dets: list = []
    face_lock = threading.Lock()
    face_generation = [0]        # bumped on each completed YuNet pass
    pending_activity_dets: list = []
    activity_lock = threading.Lock()
    pending_weapon_dets: list = []
    weapon_lock = threading.Lock()
    weapon_generation = [0]     # bumped on each completed YOLO pass
    weapon_latency = deque(maxlen=50)
    face_latency = deque(maxlen=50)
    activity_latency = deque(maxlen=50)

    def _latest_frame(last_seq):
        """Return (frame, sequence) only when a new frame has arrived."""
        with session.frame_lock:
            f = session.latest_frame
            seq = session.latest_frame_sequence
        if f is None or seq == last_seq:
            return None, last_seq
        return f, seq

    def weapon_worker():
        """
        Run YOLO off the main loop.
        YOLOv10-L costs ~180 ms at 320 px, so keeping it inline would pin the
        loop far above its 30 ms budget. Results are published for the loop to
        consume at whatever rate it is running.
        """
        last_seq = 0
        while session.active:
            started_at = time.monotonic()
            try:
                f, last_seq = _latest_frame(last_seq)
                if f is not None:
                    inference_started_at = time.monotonic()
                    dets = detect_weapons(f, session)
                    weapon_latency.append((time.monotonic() - inference_started_at) * 1000)
                    with weapon_lock:
                        pending_weapon_dets.clear()
                        pending_weapon_dets.extend(dets)
                        weapon_generation[0] += 1
            except Exception as e:
                print(f"[WeaponWorker] Error (continuing): {e}")
            session.stop_event.wait(
                max(0.0, WEAPON_DETECTION_PERIOD_SECONDS - (time.monotonic() - started_at))
            )

    def face_worker():
        """Run YuNet face detection at a bounded cadence on the latest frame."""
        last_seq = 0
        while session.active:
            started_at = time.monotonic()
            try:
                f, last_seq = _latest_frame(last_seq)
                if f is not None:
                    inference_started_at = time.monotonic()
                    dets = detect_faces(f, session.face_tracker)
                    dets.extend(match_hit_list(f, dets, session))
                    face_latency.append((time.monotonic() - inference_started_at) * 1000)
                    with face_lock:
                        pending_face_dets.clear()
                        pending_face_dets.extend(dets)
                        face_generation[0] += 1
            except Exception as e:
                print(f"[FaceWorker] Error (continuing): {e}")
            session.stop_event.wait(
                max(0.0, FACE_DETECTION_PERIOD_SECONDS - (time.monotonic() - started_at))
            )

    def activity_worker():
        """Run I3D only when the clip contains enough new frames."""
        while session.active:
            started_at = time.monotonic()
            try:
                inference_started_at = time.monotonic()
                dets = detect_suspicious_activity_i3d(session)
                if dets is not SKIPPED_CLIP:
                    activity_latency.append((time.monotonic() - inference_started_at) * 1000)
                if dets:
                    with activity_lock:
                        pending_activity_dets.clear()
                        pending_activity_dets.extend(dets)
            except Exception as e:
                print(f"[ActivityWorker] Error (continuing): {e}")
            session.stop_event.wait(
                max(0.0, ACTIVITY_DETECTION_PERIOD_SECONDS - (time.monotonic() - started_at))
            )

    threading.Thread(target=weapon_worker,   daemon=True).start()
    threading.Thread(target=face_worker,     daemon=True).start()
    threading.Thread(target=activity_worker, daemon=True).start()

    frame_count = 0
    latency_window = deque(maxlen=100)
    last_frame_sequence = 0
    last_weapon_generation = 0
    last_face_generation = 0
    last_loop_at = 0.0

    while session.active:
        with session.frame_lock:
            frame = session.latest_frame
            current_frame_sequence = session.latest_frame_sequence

        if frame is None:
            time.sleep(0.01)
            continue

        # ── Skip duplicate frames ──────────────────────────────────────
        # The frame_grabber runs in its own thread and updates latest_frame
        # when a new frame arrives from the stream.  Without this guard,
        # the detection loop (YOLO is fast on GPU) can run YOLO multiple
        # times on the same frame – wasting CPU and filling the I3D
        # buffer with identical frames which corrupts clip-based inference.
        if current_frame_sequence == last_frame_sequence:
            time.sleep(0.005)     # yield CPU; new frame expected in ~67 ms
            continue
        last_frame_sequence = current_frame_sequence

        # ── Cap the loop rate ──────────────────────────────────────────
        # A VOD/HLS source can deliver frames far faster than real time (this
        # loop was observed at ~76 fps from a 15 fps stream). Every extra
        # iteration copies a frame and takes locks the detector threads need,
        # so the loop is bounded to a sane display rate.
        elapsed_since_loop = time.monotonic() - last_loop_at
        if elapsed_since_loop < MAIN_LOOP_MIN_PERIOD_SECONDS:
            session.stop_event.wait(MAIN_LOOP_MIN_PERIOD_SECONDS - elapsed_since_loop)
        last_loop_at = time.monotonic()

        frame_count += 1
        t_start = time.time()

        # ── 1. Feed I3D clip buffer ──
        # Downscale before buffering. The R3D-18 transform resizes to 128x171
        # and centre-crops 112 anyway, so storing full-resolution copies only
        # burned memory bandwidth the detector threads need. Buffering at the
        # transform's own resize size keeps the crop geometry identical.
        with session.i3d_buffer_lock:
            session.i3d_frame_buffer.append(
                cv2.resize(frame, I3D_BUFFER_SIZE, interpolation=cv2.INTER_AREA)
            )
            session.i3d_frame_count += 1

        # ── 2. Consume latest results from background detector threads ──
        with weapon_lock:
            weapon_dets = list(pending_weapon_dets)
            current_weapon_generation = weapon_generation[0]
        # Only count a weapon pass once, even though its result is reused for
        # overlay across several loop iterations.
        new_weapon_pass = current_weapon_generation != last_weapon_generation
        last_weapon_generation = current_weapon_generation
        with face_lock:
            face_dets = list(pending_face_dets)
            current_face_generation = face_generation[0]
        new_face_pass = current_face_generation != last_face_generation
        last_face_generation = current_face_generation
        with activity_lock:
            activity_dets = list(pending_activity_dets)
            if activity_dets:
                pending_activity_dets.clear()   # consume once so we don't re-alert

        # ── 3. Ensemble Decision ──
        all_dets, ens_conf = ensemble_decision(weapon_dets, activity_dets, face_dets)

        t_end = time.time()
        latency_ms = (t_end - t_start) * 1000
        latency_window.append(latency_ms)

        # ── 5. Update metrics ──
        with metrics_lock:
            session.frame_count = frame_count
            ensemble_metrics["total_frames_processed"] += 1
            if new_weapon_pass:
                ensemble_metrics["weapons_detected"] += len(weapon_dets)
            ensemble_metrics["suspicious_activities"] += len(activity_dets)
            ensemble_metrics["faces_detected"] += sum(
                1 for detection in face_dets if detection.get("type") == "face"
            ) if new_face_pass else 0
            ensemble_metrics["unique_persons"] = sum(
                item.face_tracker.unique_count for item in sessions.values()
            )
            ensemble_metrics["avg_inference_latency_ms"] = round(
                float(np.mean(latency_window)), 2
            )
            if weapon_latency:
                ensemble_metrics["avg_weapon_latency_ms"] = round(float(np.mean(weapon_latency)), 2)
            if face_latency:
                ensemble_metrics["avg_face_latency_ms"] = round(float(np.mean(face_latency)), 2)
            if activity_latency:
                ensemble_metrics["avg_activity_latency_ms"] = round(float(np.mean(activity_latency)), 2)
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

            if det_key in session.last_detections:
                if current_time - session.last_detections[det_key] < dedup_window:
                    continue

            session.last_detections[det_key] = current_time
            send_detection_to_backend(
                det,
                session.location,
                confidence,
                frame,
                session.user_id,
                session.camera_id,
                session.camera_name,
            )

    print("[AttackShield AI] Ensemble detection stopped")


# ═══════════════════════════════════════════════════════════════════
# API Endpoints
# ═══════════════════════════════════════════════════════════════════

@app.post("/start-detection")
async def start_detection(request: DetectionRequest, background_tasks: BackgroundTasks):
    session_id = request.camera_id or "default"
    with sessions_lock:
        previous = sessions.get(session_id)
        if previous is not None:
            previous.stop_event.set()
        session = DetectionSession(
            session_id,
            request.location,
            request.user_id,
            request.camera_name,
            request.hit_list,
        )
        sessions[session_id] = session

    # If models aren’t loaded yet (e.g. startup hasn’t finished), load them
    # now in a thread-pool executor so we don’t block the event loop.
    if yolo_model is None or weapon_verifier_model is None or i3d_model is None or face_detector is None:
        print("[Detection] Models not ready yet – loading now (non-blocking)...")
        loop = asyncio.get_event_loop()
        await loop.run_in_executor(None, load_yolo_model)
        await loop.run_in_executor(None, load_i3d_model)
        await loop.run_in_executor(None, load_face_detector)

    background_tasks.add_task(
        process_stream,
        session,
        request.rtsp_url,
    )

    return {
        "message": "Ensemble detection started (Weapon + Activity + Face)",
        "camera_id": session_id,
    }


@app.post("/stop-detection")
async def stop_detection(request: StopDetectionRequest | None = None):
    camera_id = request.camera_id if request else None
    with sessions_lock:
        if camera_id:
            session = sessions.pop(camera_id, None)
            if session is not None:
                session.stop_event.set()
        else:
            for session in sessions.values():
                session.stop_event.set()
            sessions.clear()
    return {"message": "Detection stopped", "camera_id": camera_id}


@app.get("/models")
async def list_models():
    return {"success": True, "data": model_pipeline.status()}


@app.post("/models/register", dependencies=[Depends(require_model_admin)])
async def register_model(request: RegisterModelRequest):
    artifact = model_pipeline.register(request.name, request.version, request.path, request.sha256)
    return {"success": artifact.status != "invalid", "data": artifact}


@app.post("/models/validate/{name}", dependencies=[Depends(require_model_admin)])
async def validate_model(name: str):
    try:
        artifact = model_pipeline.validate(name)
    except KeyError:
        return {"success": False, "error": "Model is not registered"}
    return {"success": artifact.status != "invalid", "data": artifact}


@app.post("/models/activate", dependencies=[Depends(require_model_admin)])
async def activate_model(request: ActivateModelRequest):
    try:
        artifact = model_pipeline.activate(request.name)
    except KeyError:
        return {"success": False, "error": "Model is not registered"}
    except ValueError as error:
        return {"success": False, "error": str(error)}
    return {"success": True, "data": artifact, "restart_required": True}


@app.get("/health")
async def health():
    models = {
        "yolov10": {"loaded": yolo_model is not None, "error": model_errors.get("yolo")},
        "yolo11n_knife_verifier": {"loaded": weapon_verifier_model is not None, "error": model_errors.get("yolo")},
        "i3d_r3d18": {"loaded": i3d_model is not None, "error": model_errors.get("i3d")},
        "yunet_face_detection": {"loaded": face_detector is not None, "error": model_errors.get("face_detector")},
    }
    return {
        "status": "ready" if all(item["loaded"] for item in models.values()) else "degraded",
        "service": "AttackShield AI",
        "device": DEVICE,
        "weapon_backend": weapon_backend,
        "i3d_backend": i3d_backend,
        "models": models,
        "active_cameras": list(sessions),
        "model_management": model_pipeline.status(),
        "time": datetime.now().isoformat(),
    }


@app.get("/metrics")
async def get_metrics():
    """Return ensemble detection metrics for the dashboard."""
    with metrics_lock:
        return {
            "success": True,
            "data": {
                **ensemble_metrics,
                "active_cameras": list(sessions),
                "camera_status": {
                    camera_id: {
                        "frames_processed": session.frame_count,
                        "last_frame_at": session.last_frame_at,
                        "stream_error": session.stream_error,
                        "active": session.active,
                    }
                    for camera_id, session in sessions.items()
                },
            },
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
