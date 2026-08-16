# Task 5 — Finish the API layer (remaining controllers + Program.cs)

## Context
I am porting a Node/Express + Socket.IO backend to a .NET 8 Web API. The solution lives at `Backend.NET/` with 4 projects: `AttackShield.Core`, `AttackShield.Infrastructure`, `AttackShield.Api`, and a test project. The original Node backend is at `Backend/`.

**Already done and building:**
- Core: all entities (`User`, `Authority`, `Detection`, `Alert`, `Notification`, `Stats`), DTOs, interfaces
- Infrastructure: `MongoContext`, generic `MongoRepository<T>`, all 6 repositories, services (`AiServiceClient`, `BCryptPasswordHasher`, `JwtTokenService`, `RtspUrlBuilder`, `FfmpegStreamManager` split into `.cs`/`.Process.cs`/`.Args.cs`), and `DependencyInjection.cs` (`AddInfrastructure`)
- API so far: `Hubs/DetectionHub.cs`, `Hubs/IDetectionBroadcaster.cs`, `Hubs/SignalRDetectionBroadcaster.cs`, `Controllers/ApiControllerBase.cs`, `Controllers/AuthController.cs` + `AuthController.Account.cs`, `Controllers/DetectionsController.cs`

**Infrastructure compiles with 0 errors. API layer is incomplete.**

## What to build
Read the original Node controllers/routes under `Backend/controllers/` and `Backend/routes/` first, then port these controllers into `Backend.NET/src/AttackShield.Api/Controllers/`, matching the existing style (see `AuthController.cs` and `DetectionsController.cs` for the response-envelope pattern `{ success, ... }`, `ApiControllerBase`, claim-based `CurrentUserId`/`CurrentUserRole`):

1. **DashboardController** (`api/dashboard`) — `stats`, `activity`, `camera-status`, `metrics`, `trust-score`, `reset` (admin), plus `detection` POST. Stats are computed from Mongo counts with AI-service values as supplement; metrics/trust-score pass through `IAiServiceClient` raw JSON with the same fallback defaults as the Node version. Optional-auth on stats/activity/metrics/trust-score.
2. **NotificationsController** (`api/notifications`) — list (newest first), unread-count, mark-all-read, clear, get single, create, mark single read, delete. Note: the original mixes a buggy in-memory array with the DB model — make this consistently DB-backed via `INotificationRepository`.
3. **SettingsController** (`api/settings`) — get + update. Port the nested-group merge AND the flat convenience keys (`notificationsEnabled`, `soundEnabled`, `vibrationEnabled`, `detectionSensitivity`, `alertThreshold`, `darkMode`, `autoStartMonitoring`) and the `toFlat` mapping. Authenticated.
4. **AlertsController** (`api/alerts`) — all routes require role `authority`/`senior_authority`/`admin`. `new`, `my-active`, `history` (with `type`/`startDate`/`endDate`/`q` filters), `:id/accept`, `:id/dismiss`, `:id/resolve`.
5. **CamerasController** (`api/cameras`) — get (primary + embedded extras), add (build RTSP via `IRtspUrlBuilder` when only camera details given), delete (`primary` clears fields; otherwise remove from embedded `Cameras` array). Authenticated.
6. **StreamController** (`api/stream`) — `start`, `stop`, `start-all`, `stop-all`, `webcam`, `status` via `IStreamManager`; `GET mjpeg/{cameraId}` writes the multipart MJPEG stream (no auth — `<img>` can't send headers); serve HLS files statically from the streams root with the CORS + no-cache + content-type headers the Node version sets. HLS URL shape: `/api/stream/hls/{cameraId}/index.m3u8`.

If any file would exceed the write tool's size limit, split it into partial-class files (as was done for `AuthController`/`FfmpegStreamManager`). Comply silently with size limits — do not ask to switch tools.

## Program.cs
Replace the weather-forecast template `Program.cs` entirely. Wire up:
- Serilog from config
- `AddInfrastructure(builder.Configuration)`
- Controllers + JSON options
- JWT bearer auth reading the `id`/`role` claims minted by `JwtTokenService`; role-based authorization
- Google OAuth (config `Google:ClientId`/`ClientSecret`) + the `/api/auth/google` and `/api/auth/google/callback` endpoints (port `googleCallback` from `authController.js` — redirects to frontend with token + user)
- CORS from `Cors:AllowedOrigins`
- SignalR: register `IDetectionBroadcaster`, map `DetectionHub` at `/socket` (or the path the frontend expects — check `Backend/server.js`)
- Static file serving for the HLS streams root (`Stream:StreamsRoot`)
- App shutdown hook calling `IStreamManager.StopAllAsync()`
- Flag if any network-exposed endpoint ends up unauthenticated that shouldn't be.

## Verify
Run `dotnet build` on the solution and fix all errors. Report what builds and what could not be verified (e.g. runtime Mongo/FFmpeg not available). Keep comments matching the surrounding density. Do not commit unless I ask.
