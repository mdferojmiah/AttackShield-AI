# AttackShield AI System Details

## Architecture

```text
React frontend (3000)
    | REST + SignalR
ASP.NET Core API (5217)
    | MongoDB persistence
    | FFmpeg HLS/MJPEG streaming
    | HTTP detection control/results
FastAPI AI service (8000)
    | Model management and validation registry
    | Fusion and rule engine
```

The ASP.NET Core application is the only backend. The earlier Node.js implementation has been retired.

## Frontend

`Frontend/` contains the React 18, TypeScript, Vite, and Tailwind client. It uses Axios for authenticated REST requests and SignalR for detection overlays, alerts, notifications, and detection lifecycle events.

Primary workflows include authentication, dashboard monitoring, Live Feed camera configuration, alerts, notifications, settings, and per-user hit-list management.

## Backend

`Backend.NET/` contains the .NET 8 solution:

- `AttackShield.API`: controllers, authentication, SignalR, static stream serving, and application startup.
- `AttackShield.Core`: DTOs, entities, and service/repository interfaces.
- `AttackShield.Infrastructure`: MongoDB repositories, JWT/password services, AI HTTP integration, RTSP URL generation, and FFmpeg stream management.
- `AttackShield.Infrastructure/Services/NotificationFanout.cs`: best-effort email and webhook/mobile-gateway delivery after alert persistence and SignalR broadcast.
- `AttackShield.Tests`: controller, persistence, and service tests.

The API runs at `http://localhost:5217`. Its health endpoint is `GET /api/health`, and its SignalR hub is `/hubs/detection`.

## AI Service

`AIService/` is a FastAPI service running at `http://localhost:8000`. It reads HLS or RTSP streams and performs:

- YOLO-based weapon detection and verification.
- R3D-18/Kinetics suspicious-activity classification.
- RetinaFace person/face localization.
- OpenCV SFace hit-list matching.
- Screenshot evidence generation.

Detection results are sent to `POST http://localhost:5217/api/detections/receive` by default.

### AI model lifecycle

`AIService/model_management.py` maintains an atomic JSON registry at `AIService/model_registry.json`. At startup it registers the deployed weapon, knife-verifier, and face-recognition artifacts and validates their file paths. The FastAPI endpoints are:

- `GET /models`: registry and validation status.
- `POST /models/register`: register a model path, version, and optional SHA-256 checksum.
- `POST /models/validate/{name}`: validate a registered artifact.
- `POST /models/activate`: mark a validated artifact as active.

Activation reports `restart_required: true`. The current Torch/Ultralytics objects are loaded at service startup, so activation is deliberately registry-only until a controlled service restart or rolling deployment replaces the in-memory model.

### Fusion and rules

`AIService/fusion_engine.py` owns the ensemble decision. It combines weapon, activity, and face detections, assigns priority and threat levels, calculates the aggregate confidence, and determines whether the result crosses the alert threshold. The stream loop consumes this decision instead of embedding those rules in frame-processing code.

### Notification fan-out

After an alert is persisted and the existing SignalR events are sent, the API publishes a normalized notification payload to configured channels. Delivery failures are logged and isolated from detection persistence. Configure `Notifications:Smtp` for email and `Notifications:Webhooks` for a mobile push gateway, SMS gateway, or other HTTPS notification provider. Both channels are disabled by default; credentials should be supplied through environment-specific configuration or secret storage.

## Camera And Streaming Flow

1. An authenticated user adds a camera from Live Feed.
2. The camera may use a direct RTSP URL or generated settings for Generic, Hikvision, Dahua, or Meari devices.
3. ASP.NET starts FFmpeg and exposes HLS and MJPEG outputs.
4. The browser displays MJPEG while the AI service consumes the stream for inference.
5. Detection results are persisted in MongoDB and broadcast through SignalR.

## Alert Policy

Weapon alerts and notifications require at least 60% confidence. Weapon and suspicious-activity notifications include screenshot evidence when available. Hit-list matches use repeated confirmation and create high-priority alerts with user-scoped reference data.

## Required Software

- .NET 8 SDK
- Node.js and npm for the frontend toolchain
- Python with the packages in `AIService/requirements.txt`
- MongoDB
- FFmpeg available on `PATH` or through backend configuration
