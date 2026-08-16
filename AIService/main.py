"""
AttackShield AI Service
Multi-Model Ensemble:
  1. YOLOv10  – Weapon Detection (knife, pistol, gun)
  2. I3D (Inflated 3D ConvNet) – Suspicious Activity / Action Recognition
  3. RetinaFace – High-accuracy Face Detection
Aligns with the AttackShield AI project proposal.
"""

from fastapi import FastAPI, BackgroundTasks
from pydantic import BaseModel, Field
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
import base64
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
WEAPON_VERIFIER_PATH = os.path.join(BASE_DIR, 'yolo11n.pt')
SFACE_MODEL_PATH = os.path.join(BASE_DIR, 'face_recognition_sface_2021dec.onnx')
BACKEND_URL = os.environ.get(
    'BACKEND_URL',
    "http://localhost:5217/api/detections/receive"
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

CONFIDENCE_THRESHOLD = 0.60            # Only high-confidence weapon detections alert
KNIFE_CONFIDENCE_THRESHOLD = 0.70      # Knives are especially prone to edge/texture false positives
WEAPON_CONFIRMATION_FRAMES = 3         # Require repeated spatial agreement before alerting
WEAPON_CONFIRMATION_IOU = 0.30
WEAPON_DETECTION_INTERVAL = 2         # Run YOLO on every second unique frame on CPU
FACE_DETECTION_INTERVAL = 2            # Run face detection every 2 frames
FACE_DETECTION_PERIOD_SECONDS = 2.0    # RetinaFace costs ~3.6 s at 640x480 on CPU
ACTIVITY_DETECTION_INTERVAL = 8        # Run I3D every 8 frames (half-clip overlap)

# Per-type deduplication windows (seconds)
DUPLICATE_WINDOWS = {
    "weapon":             10,   # Resend weapon alert every 10 s
    "suspicious_activity": 30,  # Resend activity alert every 30 s
    "face":               3,    # Resend face bbox every 3 s (keeps overlay alive)
    "hit_list":          60,
}
I3D_CLIP_LENGTH = 16                   # Number of frames per I3D clip
I3D_CONFIDENCE_THRESHOLD = 0.35        # Avoid low-confidence action false positives


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
weapon_verifier_model = None
i3d_model = None
i3d_weights = None
i3d_categories = None
i3d_preprocess = None
retinaface_loaded = False
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
    hit_list: list[dict] = Field(default_factory=list)


class StopDetectionRequest(BaseModel):
    camera_id: str | None = None


# ═══════════════════════════════════════════════════════════════════
# Model Loaders
# ═══════════════════════════════════════════════════════════════════

def load_yolo_model():
    """Load YOLOv10 weapon detection model."""
    global yolo_model, weapon_verifier_model
    if yolo_model is not None and weapon_verifier_model is not None:
        return
    try:
        print(f"[YOLO] Loading weapon model from {MODEL_PATH}")
        yolo_model = YOLO(MODEL_PATH)
        weapon_verifier_model = YOLO(WEAPON_VERIFIER_PATH)
        model_errors.pop("yolo", None)
        print("[YOLO] Weapon model loaded")
    except Exception as exc:
        model_errors["yolo"] = str(exc)
        raise


def load_i3d_model():
    """
    Load I3D (Inflated 3D ConvNet) for action recognition.
    Uses torchvision's R3D-18 pretrained on Kinetics-400.
    R3D-18 is an I3D-family model (3D ResNet with inflated convolutions).
    """
    global i3d_model, i3d_weights, i3d_categories, i3d_preprocess

    if i3d_model is not None:
        return
    try:
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
        model_errors.pop("i3d", None)
        print(f"[I3D] Model loaded - {len(i3d_categories)} action categories")
    except Exception as exc:
        model_errors["i3d"] = str(exc)
        raise


def load_retinaface():
    """
    RetinaFace initialization.
    The retinaface package auto-downloads its weights on first use.
    We just set a flag so we know it's ready.
    """
    global retinaface_loaded
    if retinaface_loaded:
        return
    try:
        print("[RetinaFace] Initializing face detector...")
        # Warm-up: run one detection on a dummy image to trigger weight download
        dummy = np.zeros((112, 112, 3), dtype=np.uint8)
        RF.detect_faces(dummy)
        retinaface_loaded = True
        model_errors.pop("retinaface", None)
        print("[RetinaFace] Model loaded")
    except Exception as exc:
        model_errors["retinaface"] = str(exc)
        raise


# ═══════════════════════════════════════════════════════════════════
# Detection Functions (Ensemble Components)
# ═══════════════════════════════════════════════════════════════════

def _confirmed_by_coco_knife_model(frame, candidate_bbox):
    results = weapon_verifier_model(frame, conf=0.25, classes=[43], verbose=False)
    frame_height, frame_width = frame.shape[:2]
    for result in results:
        if not hasattr(result, "boxes") or result.boxes is None:
            continue
        for box in result.boxes:
            x1, y1, x2, y2 = [float(value) for value in box.xyxy[0]]
            verifier_bbox = {
                "x": x1 / frame_width,
                "y": y1 / frame_height,
                "w": (x2 - x1) / frame_width,
                "h": (y2 - y1) / frame_height,
            }
            if FaceTracker._iou(candidate_bbox, verifier_bbox) >= WEAPON_CONFIRMATION_IOU:
                return True
    return False


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
        verbose=False,
    )
    detections = []
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
            if normalized_name == "knife" and not _confirmed_by_coco_knife_model(frame, detection["bbox"]):
                print(f"[Weapon] Rejected unverified knife candidate ({conf:.0%})")
                continue
            detections.append(detection)

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
            return detections
        if session.i3d_frame_count - session.i3d_last_processed_count < ACTIVITY_DETECTION_INTERVAL:
            return detections
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


def detect_faces_retinaface(frame, tracker):
    """
    RetinaFace deep-learning face detection.
    Returns bounding boxes + confidence for every detected face.
    Frame is downscaled before inference for speed, then bbox is re-scaled back.
    """
    detections = []
    fh, fw = frame.shape[:2]

    # Downscale to max 320px wide while keeping aspect ratio – significantly
    # speeds up RetinaFace without hurting face-detection accuracy.
    scale = min(1.0, 320 / fw)
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
        landmarks = face_data.get("landmarks", {})
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
            "landmarks": {
                key: [round(point[0] / scale), round(point[1] / scale)]
                for key, point in landmarks.items()
            } if scale < 1.0 else landmarks,
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
    for entry in session.hit_list:
        try:
            reference = _decode_data_image(entry["image_url"])
            faces = detect_faces_retinaface(reference, FaceTracker())
            if len(faces) != 1:
                print(f"[HitList] Skipping {entry.get('name')}: reference must contain one face")
                continue
            embedding = _face_embedding(reference, faces[0])
            if embedding is not None:
                session.hit_list_embeddings.append((entry, embedding))
        except Exception as exc:
            print(f"[HitList] Could not prepare {entry.get('name')}: {exc}")
    if session.hit_list_embeddings:
        print(f"[HitList] Prepared {len(session.hit_list_embeddings)} reference face(s)")


def match_hit_list(frame, face_detections, session):
    matches = []
    seen = set()
    recognizer = _load_sface()
    for face in face_detections:
        embedding = _face_embedding(frame, face)
        if embedding is None:
            continue
        best_entry, best_score = None, 0.0
        for entry, reference_embedding in session.hit_list_embeddings:
            score = float(recognizer.match(reference_embedding, embedding, cv2.FaceRecognizerSF_FR_COSINE))
            if score > best_score:
                best_entry, best_score = entry, score
        if best_entry is None or best_score < 0.45:
            continue
        entry_id = best_entry["id"]
        seen.add(entry_id)
        session.hit_list_confirmations[entry_id] = session.hit_list_confirmations.get(entry_id, 0) + 1
        if session.hit_list_confirmations[entry_id] >= 2:
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
        d.setdefault("priority", "low")
        d.setdefault("threat_level", "info")
        all_detections.append(d)
        confidences.append(d["confidence"])

    ensemble_conf = float(np.mean(confidences)) if confidences else 0.0

    return all_detections, round(ensemble_conf, 3)


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
    pending_activity_dets: list = []
    activity_lock = threading.Lock()

    def face_worker():
        """Run expensive RetinaFace at a bounded cadence on the latest frame."""
        last_frame_sequence = 0
        while session.active:
            started_at = time.monotonic()
            try:
                with session.frame_lock:
                    f = session.latest_frame
                    current_frame_sequence = session.latest_frame_sequence
                if f is not None and current_frame_sequence != last_frame_sequence:
                    last_frame_sequence = current_frame_sequence
                    dets = detect_faces_retinaface(f, session.face_tracker)
                    dets.extend(match_hit_list(f, dets, session))
                    with face_lock:
                        pending_face_dets.clear()
                        pending_face_dets.extend(dets)
            except Exception as e:
                print(f"[FaceWorker] Error (continuing): {e}")
            session.stop_event.wait(max(0.0, FACE_DETECTION_PERIOD_SECONDS - (time.monotonic() - started_at)))

    def activity_worker():
        """Run I3D only when the clip contains enough new frames."""
        while session.active:
            try:
                dets = detect_suspicious_activity_i3d(session)
                if dets:
                    with activity_lock:
                        pending_activity_dets.clear()
                        pending_activity_dets.extend(dets)
            except Exception as e:
                print(f"[ActivityWorker] Error (continuing): {e}")
            time.sleep(0.1)

    threading.Thread(target=face_worker,     daemon=True).start()
    threading.Thread(target=activity_worker, daemon=True).start()

    frame_count = 0
    latency_window = deque(maxlen=100)
    last_frame_sequence = 0

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

        frame_count += 1
        t_start = time.time()

        # ── 1. Feed I3D clip buffer ──
        with session.i3d_buffer_lock:
            session.i3d_frame_buffer.append(frame.copy())
            session.i3d_frame_count += 1

        # ── 2. Weapon detection – sample frames to reduce CPU contention ──
        weapon_dets = []
        if frame_count % WEAPON_DETECTION_INTERVAL == 0:
            weapon_dets = detect_weapons(frame, session)

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
            session.frame_count = frame_count
            ensemble_metrics["total_frames_processed"] += 1
            ensemble_metrics["weapons_detected"] += len(weapon_dets)
            ensemble_metrics["suspicious_activities"] += len(activity_dets)
            ensemble_metrics["faces_detected"] += sum(
                1 for detection in face_dets if detection.get("type") == "face"
            )
            ensemble_metrics["unique_persons"] = sum(
                item.face_tracker.unique_count for item in sessions.values()
            )
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
    if yolo_model is None or weapon_verifier_model is None or i3d_model is None or not retinaface_loaded:
        print("[Detection] Models not ready yet – loading now (non-blocking)...")
        loop = asyncio.get_event_loop()
        await loop.run_in_executor(None, load_yolo_model)
        await loop.run_in_executor(None, load_i3d_model)
        await loop.run_in_executor(None, load_retinaface)

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


@app.get("/health")
async def health():
    models = {
        "yolov10": {"loaded": yolo_model is not None, "error": model_errors.get("yolo")},
        "yolo11n_knife_verifier": {"loaded": weapon_verifier_model is not None, "error": model_errors.get("yolo")},
        "i3d_r3d18": {"loaded": i3d_model is not None, "error": model_errors.get("i3d")},
        "retinaface": {"loaded": retinaface_loaded, "error": model_errors.get("retinaface")},
    }
    return {
        "status": "ready" if all(item["loaded"] for item in models.values()) else "degraded",
        "service": "AttackShield AI",
        "models": models,
        "active_cameras": list(sessions),
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
