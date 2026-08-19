from __future__ import annotations

import hashlib
import json
import os
import tempfile
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from threading import RLock


@dataclass
class ModelArtifact:
    name: str
    version: str
    path: str
    sha256: str | None = None
    status: str = "registered"
    activated_at: str | None = None
    validation_error: str | None = None


class ModelManagementPipeline:
    """Small local model registry with validation and atomic persistence."""

    def __init__(self, registry_path: str, model_root: str):
        self.registry_path = Path(registry_path)
        self.model_root = Path(model_root).resolve()
        self._lock = RLock()
        self._models: dict[str, ModelArtifact] = {}
        self._load()

    def _load(self) -> None:
        if not self.registry_path.exists():
            return
        try:
            raw = json.loads(self.registry_path.read_text(encoding="utf-8"))
            self._models = {
                item["name"]: ModelArtifact(**item)
                for item in raw.get("models", [])
            }
        except (OSError, ValueError, TypeError, KeyError):
            self._models = {}

    def _save(self) -> None:
        self.registry_path.parent.mkdir(parents=True, exist_ok=True)
        payload = {"models": [asdict(model) for model in self._models.values()]}
        with tempfile.NamedTemporaryFile(
            mode="w", encoding="utf-8", dir=self.registry_path.parent,
            delete=False, suffix=".tmp"
        ) as handle:
            json.dump(payload, handle, indent=2)
            temp_path = Path(handle.name)
        os.replace(temp_path, self.registry_path)

    def _resolve(self, path: str) -> Path:
        candidate = Path(path)
        if not candidate.is_absolute():
            candidate = self.model_root / candidate
        return candidate.resolve()

    def register(self, name: str, version: str, path: str, sha256: str | None = None) -> ModelArtifact:
        resolved = self._resolve(path)
        artifact = ModelArtifact(name, version, str(resolved), sha256)
        with self._lock:
            self._models[name] = artifact
            self._save()
        self.validate(name)
        return self._models[name]

    def validate(self, name: str) -> ModelArtifact:
        with self._lock:
            artifact = self._models[name]
            path = Path(artifact.path)
            error = None
            if not path.is_file():
                error = f"Model file does not exist: {path}"
            elif artifact.sha256:
                digest = hashlib.sha256(path.read_bytes()).hexdigest()
                if digest.lower() != artifact.sha256.lower():
                    error = "SHA-256 checksum does not match"
            artifact.status = "valid" if error is None else "invalid"
            artifact.validation_error = error
            self._save()
            return artifact

    def activate(self, name: str) -> ModelArtifact:
        with self._lock:
            artifact = self._models[name]
            if artifact.status not in {"valid", "active"}:
                raise ValueError("Model must pass validation before activation")
            artifact.status = "active"
            artifact.activated_at = datetime.now(timezone.utc).isoformat()
            self._save()
            return artifact

    def list(self) -> list[ModelArtifact]:
        with self._lock:
            return list(self._models.values())

    def status(self) -> dict[str, object]:
        models = self.list()
        return {
            "registered": len(models),
            "valid": sum(model.status in {"valid", "active"} for model in models),
            "active": [model.name for model in models if model.status == "active"],
            "models": [asdict(model) for model in models],
        }
