# AttackShield AI

AttackShield AI is an intelligent CCTV monitoring system built from three active services:

- `Frontend/`: React, TypeScript, and Vite web client.
- `Backend.NET/`: ASP.NET Core 8 REST API, SignalR hub, MongoDB persistence, and FFmpeg stream management.
- `AIService/`: FastAPI detection service for weapons, suspicious activity, faces, and hit-list matching.

## Features

- User and authority authentication, including Google OAuth support.
- Camera management from Live Feed using direct RTSP URLs or IP-camera presets.
- HLS and low-latency MJPEG streaming through FFmpeg.
- Weapon and suspicious-activity detection with screenshot evidence.
- Per-user face hit lists with high-priority alerts.
- Dashboard statistics, alerts, notifications, settings, and realtime SignalR updates.

## Local Services

| Service | Default URL |
| --- | --- |
| Frontend | `http://localhost:3000` |
| ASP.NET API | `http://localhost:5217` |
| AI service | `http://localhost:8000` |

MongoDB and FFmpeg must be available to the ASP.NET API. Environment-specific values are configured through the service configuration and `.env.example` files.

## Development

Frontend:

```powershell
npm --prefix Frontend install
npm --prefix Frontend run dev
```

Backend:

```powershell
dotnet run --project Backend.NET/src/AttackShield.API/AttackShield.API.csproj
```

AI service:

```powershell
python -m pip install -r AIService/requirements.txt
python AIService/main.py
```

Run backend tests:

```powershell
dotnet test Backend.NET/tests/AttackShield.Tests/AttackShield.Tests.csproj
```
