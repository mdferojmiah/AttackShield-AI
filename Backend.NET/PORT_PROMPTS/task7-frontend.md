# Task 7 — Repoint the frontend to the .NET backend (SignalR swap)

## Context
The Node/Express + Socket.IO backend is being replaced by a .NET 8 Web API (`Backend.NET/`). The .NET API keeps the same REST routes (`/api/auth`, `/api/dashboard`, `/api/notifications`, `/api/settings`, `/api/alerts`, `/api/cameras`, `/api/detections`, `/api/stream`) and the same `{ success, ... }` response shapes. Real-time moved from **Socket.IO to SignalR**, but the event names were kept identical: `detection-overlay`, `weapon-detected`, `notification-created`, `alert-created`, `detection-started`, and the client-invoked `start-detection` (now a hub method `StartDetection`).

The frontend lives at the repo root (check for a `Frontend/`, `client/`, `web/`, or React Native / Expo app directory — find it first). Read how it currently connects before changing anything.

## What to do
1. **Find the API base URL config** (env var / constants file, e.g. `API_BASE`, `BASE_URL`, axios instance). Confirm the .NET API's URL/port (check `Backend.NET/src/AttackShield.Api/Properties/launchSettings.json` and `appsettings.json` — likely `http://localhost:5000`). Repoint it. Keep REST calls unchanged since routes/shapes match — verify a couple against the ported controllers.
2. **Swap the Socket.IO client for the SignalR client:**
   - Install `@microsoft/signalr`. Remove `socket.io-client` usage.
   - Replace `io(url)` connection setup with a `HubConnectionBuilder().withUrl(<hub path>).withAutomaticReconnect().build()` and `.start()`. Confirm the hub path the .NET side maps (check `Backend.NET/src/AttackShield.Api/Program.cs` — look for `MapHub<DetectionHub>(...)`).
   - Replace `socket.on('event', handler)` with `connection.on('event', handler)` — event names are unchanged.
   - Replace `socket.emit('start-detection', payload)` with `connection.invoke('StartDetection', payload)`. Keep the payload shape (`streamUrl`, `location`, `user`, `cameraName`, `cameraId`).
   - Preserve reconnect/cleanup behaviour and any auth token passing (SignalR uses `accessTokenFactory` in the `withUrl` options if the hub needs auth).
3. Keep changes minimal and match existing frontend code style. Do not restructure components beyond what the transport swap needs.

## Verify
Build/lint the frontend. If a dev server can run, start it and confirm no console errors on connect and that REST calls hit the .NET API. Report what you changed, what you verified, and anything you could not verify (e.g. backend not running). Do not commit unless I ask.
