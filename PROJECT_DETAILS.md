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
