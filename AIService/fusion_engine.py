from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class FusionDecision:
    detections: list[dict]
    confidence: float
    threat_level: str
    should_alert: bool


class FusionRuleEngine:
    """Score-level fusion and policy decisions kept independent from model code."""

    def __init__(self, alert_threshold: float = 0.60):
        self.alert_threshold = alert_threshold

    def evaluate(self, weapon_dets: list[dict], activity_dets: list[dict], face_dets: list[dict]) -> FusionDecision:
        detections = []
        for detection in weapon_dets:
            detections.append({**detection, "priority": "high", "threat_level": "critical"})
        for detection in activity_dets:
            detections.append({**detection, "priority": "medium", "threat_level": "warning"})
        for detection in face_dets:
            # Hit-list matches arrive alongside plain faces because both come from
            # the face worker. They carry their own high/critical grading, so they
            # must not be flattened to the low/info used for anonymous faces.
            if detection.get("type") == "hit_list":
                detections.append({
                    **detection,
                    "priority": detection.get("priority", "high"),
                    "threat_level": detection.get("threat_level", "critical"),
                })
            else:
                detections.append({**detection, "priority": "low", "threat_level": "info"})

        hit_list_dets = [d for d in face_dets if d.get("type") == "hit_list"]
        confidences = [float(item["confidence"]) for item in detections]
        confidence = sum(confidences) / len(confidences) if confidences else 0.0
        threat_level = (
            "critical" if weapon_dets or hit_list_dets
            else "warning" if activity_dets
            else "info"
        )
        should_alert = bool(weapon_dets or activity_dets or hit_list_dets) and confidence >= self.alert_threshold
        return FusionDecision(detections, round(confidence, 3), threat_level, should_alert)
