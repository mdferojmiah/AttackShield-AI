# AttackShield AI - Complete Project Details

## Project Overview

The **AttackShield AI** system is a real-time security surveillance application that uses a **multi-model ensemble architecture** for intelligent threat detection in live CCTV camera feeds. The ensemble combines:

- **YOLOv10** for weapon detection (knives, pistols, guns)
- **Motion-based spatiotemporal analysis** for suspicious activity detection
- **Haar/RetinaFace cascade** for face detection and biometric identification

When a threat is detected, the system automatically alerts nearby law enforcement authorities in real time. A **Trust-Based Governance Layer** computes trust scores across authentication consistency, anomaly frequency, model confidence stability, communication integrity, and policy compliance.

The project follows a **microservices architecture** with four distinct components:

| Component             | Role                  | Technology Stack                                       | Port  |
|-----------------------|-----------------------|--------------------------------------------------------|-------|
| **Frontend**           | Web Dashboard         | React 18, Vite, TypeScript, Tailwind CSS               | 3000  |
| **Backend**            | REST API + WebSocket  | Node.js, Express.js, MongoDB, Socket.IO                | 5000  |
| **AIService**          | AI/ML Ensemble Engine | Python, FastAPI, YOLOv10, OpenCV, Haar Cascade, PyTorch| 8000  |
| **Yolov10**            | ML Model Storage      | YOLOv10 pre-trained weights (`model.pt`)               | N/A   |

---

## 1. Frontend — `WeaponDetectionApp/`

### What It Is
A **React Native CLI** mobile application (Android & iOS) that serves as the user-facing interface. It is the **only component the end user directly interacts with**.

### Technology Stack
- **React Native 0.76.5** (CLI, not Expo)
- **TypeScript**
- **React Navigation** (Stack + Bottom Tabs)
- **Socket.IO Client** (real-time communication)
- **Axios** (HTTP requests)
- **VLC Media Player** (`react-native-vlc-media-player`) for RTSP live stream playback
- **AsyncStorage** (local data persistence)
- **React Native Vector Icons** (UI icons)
- **React Native Reanimated** (animations)

### What It Does
- **User Authentication**: Login, signup (for both regular users and authorities)
- **Live Camera Feed**: Displays real-time RTSP camera streams using VLC player
- **Dashboard**: Shows detection statistics (total weapons detected, alerts sent, accuracy)
- **Real-Time Alerts**: Receives weapon detection alerts via Socket.IO and displays them instantly
- **Notifications**: Lists all past detection notifications
- **Camera Management**: Users can add multiple CCTV cameras with auto-generated RTSP URLs
- **Settings**: Notification preferences, detection sensitivity, theme (dark/light mode)
- **Authority Dashboard**: Separate UI for law enforcement to accept, dismiss, or resolve alerts

### Two User Roles
1. **Regular User (CCTV Owner)**: Registers cameras, views live feeds, receives weapon detection notifications, and views dashboard statistics.
2. **Authority (Law Enforcement)**: Receives weapon alerts, can accept/dismiss/resolve them, and views response history.

### Key Files
| File | Purpose |
|------|---------|
| `App.tsx` | Root component — wraps app in SocketProvider, AuthProvider, ThemeProvider |
| `src/navigation/AppNavigator.tsx` | Navigation setup — auth screens vs main tabs, role-based routing |
| `src/utils/api.ts` | All HTTP API calls (Auth, Dashboard, Notifications, Alerts, Cameras, Settings) |
| `src/utils/config.ts` | API endpoint configuration (host, port, URLs) |
| `src/utils/SocketContext.tsx` | Socket.IO connection manager — emits `start-detection` events |
| `src/utils/AuthContext.tsx` | Global authentication state (login, logout, token management) |
| `src/utils/storage.ts` | AsyncStorage wrapper for persisting user data and tokens |
| `src/utils/ThemeContext.tsx` | Dark/light theme management |
| `src/screens/user/DashboardScreen.tsx` | User dashboard with stats, activity feed, camera status |
| `src/screens/user/LiveFeedScreen.tsx` | Live RTSP camera viewer with VLC player + weapon detection overlay |
| `src/screens/user/AllNotificationsScreen.tsx` | Full notification history list |
| `src/screens/user/ExploreScreen.tsx` | Explore/discovery screen |
| `src/screens/authority/AuthorityDashboardScreen.tsx` | Authority dashboard — new alerts, active alerts |
| `src/screens/authority/AuthorityAlertDetailsScreen.tsx` | Detailed alert view for authorities |
| `src/screens/authority/AuthorityHistoryScreen.tsx` | Authority response history with filters |

### How It Connects
- **HTTP REST API** → Communicates with `Backend` on port `5000` for all CRUD operations
- **Socket.IO** → Maintains a persistent WebSocket connection to `Backend` for real-time events
- **RTSP Streams** → Connects directly to CCTV cameras via RTSP URLs for live video playback

---

## 2. Backend (REST API + WebSocket Server) — `Backend/`

### What It Is
A **Node.js/Express.js** server that acts as the **central API and real-time communication hub**. It sits between the mobile app and the AI service, managing all data flow.

### Technology Stack
- **Node.js** with **Express.js 5.1**
- **MongoDB** with **Mongoose 8.x** (database)
- **Socket.IO 4.8** (real-time WebSocket server)
- **JWT** (`jsonwebtoken`) for authentication
- **bcryptjs** for password hashing
- **Helmet** (security headers)
- **express-rate-limit** (API rate limiting)
- **CORS** enabled
- **Axios** (for communicating with AI service)

### What It Does

#### Authentication (`/api/auth`)
- **POST `/signup/user`** — Register a new user with camera/RTSP details
- **POST `/signup/authority`** — Register a law enforcement authority
- **POST `/login`** — Authenticate users/authorities, return JWT token
- Auto-generates RTSP URLs from camera IP, brand, username, and password

#### Dashboard (`/api/dashboard`)
- **GET `/stats`** — Returns weapon detection count, alerts sent, model accuracy
- **GET `/activity`** — Returns recent 10 detection activities

#### Cameras (`/api/cameras`)
- **GET `/`** — List user's cameras (primary + additional)
- **POST `/`** — Add a new camera to user's account

#### Notifications (`/api/notifications`)
- **GET `/`** — List all user notifications
- **PUT `/:id/read`** — Mark notification as read
- **PUT `/read-all`** — Mark all notifications as read
- **DELETE `/:id`** — Delete a notification

#### Alerts (`/api/alerts`) — For Authorities
- **GET `/new`** — Fetch unassigned alerts
- **GET `/my-active`** — Fetch alerts assigned to current authority
- **POST `/:id/accept`** — Accept an alert
- **POST `/:id/dismiss`** — Dismiss an alert
- **POST `/:id/resolve`** — Resolve an alert
- **GET `/history`** — View past handled alerts with filtering

#### Detections (`/api/detections`) — Core Integration Endpoint
- **POST `/receive`** — Receives weapon detection results from the AI service. This is the **most critical endpoint** because it:
  1. Validates the incoming detection data
  2. Enforces confidence threshold (≥ 0.6)
  3. Deduplicates detections within a 10-second window
  4. Saves a `Detection` record in MongoDB
  5. Creates a `Notification` for the user
  6. Creates an `Alert` for authorities
  7. Emits Socket.IO events (`weapon-detected`, `notification-created`, `alert-created`) for real-time frontend updates

#### Settings (`/api/settings`)
- **GET `/`** — Get user settings
- **PUT `/`** — Update user settings (notifications, detection, app preferences)

#### Socket.IO (Real-Time)
- Listens for `start-detection` events from the mobile app and forwards them to the AI service
- Tracks user-socket associations to manage detection sessions
- Auto-stops detection when all of a user's sockets disconnect
- Broadcasts detection events to connected clients

### Database Models
| Model | Purpose | Key Fields |
|-------|---------|------------|
| `User` | Regular user accounts | name, email, password (hashed), phone, cctvName, rtspUrl, location, cameras[], settings |
| `Authority` | Law enforcement accounts | name, email, officerId, stationName, password (hashed), department, role, assignedUsers[] |
| `Detection` | AI detection records | weaponType, location, confidence, imageUrl, cameraName, userId |
| `Notification` | User-facing notifications | type, title, description, icon, location, userId, isRead |
| `Alert` | Authority-facing alerts | type (severity), message, title, location, status (new/accepted/dismissed/resolved), assignedTo, detectionId |

### Key Files
| File | Purpose |
|------|---------|
| `server.js` | Main entry — Express setup, MongoDB connection, Socket.IO, route mounting |
| `controllers/authController.js` | User/authority registration, login, RTSP URL generation |
| `controllers/alertsController.js` | Alert CRUD for authorities |
| `controllers/dashboardController.js` | Dashboard statistics and activity feed |
| `controllers/notificationsController.js` | Notification CRUD for users |
| `controllers/camerasController.js` | Camera management per user |
| `controllers/settingsController.js` | User settings CRUD |
| `routes/detections.js` | Detection ingestion endpoint (receives from AI, fans out to all stores) |
| `services/aiService.js` | HTTP client for AI service (start/stop detection, health check) |
| `middleware/auth.js` | JWT verification, role-based authorization |
| `middleware/errorHandler.js` | Global error handling |
| `middleware/validation.js` | Request validation |

### How It Connects
- **Receives HTTP requests** from the mobile app (REST API)
- **Maintains Socket.IO connections** with the mobile app (real-time events)
- **Forwards detection requests** to the AI service via HTTP (Axios)
- **Receives detection results** from the AI service via HTTP POST to `/api/detections/receive`
- **Stores data** in MongoDB

---

## 3. AI/ML Service — `AIService/`

### What It Is
A **Python FastAPI** microservice that performs **real-time weapon detection** on live CCTV camera feeds using a **YOLOv10** deep learning model.

### Technology Stack
- **Python** with **FastAPI** (web framework)
- **Uvicorn** (ASGI server)
- **YOLOv10** via **Ultralytics** library (object detection model)
- **OpenCV** (`cv2`) for video stream capture and frame processing
- **PyTorch** + **Torchvision** (deep learning framework)
- **NumPy** and **Pillow** (image processing)

### What It Does
1. **Loads the YOLOv10 model** (`Yolov10/model.pt`) — a custom-trained model for detecting knives, pistols, and guns
2. **Opens RTSP camera stream** via OpenCV and continuously grabs frames
3. **Runs YOLOv10 inference** on each frame to detect weapons
4. **Filters results** by confidence threshold (≥ 0.6)
5. **Deduplicates detections** — same weapon type within 10 seconds is not re-reported
6. **Sends detection results** to the Backend via HTTP POST to `/api/detections/receive`

### API Endpoints
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/start-detection` | POST | Start weapon detection on an RTSP stream. Accepts `rtsp_url`, `location`, `user_id` |
| `/stop-detection` | POST | Stop the current detection session |
| `/health` | GET | Health check endpoint |

### Processing Pipeline
```
RTSP Camera Stream
       ↓
  OpenCV VideoCapture (frame_grabber thread)
       ↓
  Latest Frame Buffer (thread-safe)
       ↓
  YOLOv10 Inference (process_stream loop)
       ↓
  Filter: confidence ≥ 0.6 & class in [knife, pistol, gun]
       ↓
  Deduplication: 10-second window per weapon type
       ↓
  HTTP POST → Backend /api/detections/receive
```

### Key Design Decisions
- **Two-thread architecture**: A `frame_grabber` thread continuously captures frames from the RTSP stream, while the main processing loop always works on the **latest frame** (skipping old frames to avoid lag)
- **Background task**: Detection runs as a FastAPI `BackgroundTask` so the API remains responsive
- **Stateless per-session**: Only one detection session runs at a time (single camera)

### Key Files
| File | Purpose |
|------|---------|
| `main.py` | Full AI service — FastAPI app, YOLO model loading, frame grabbing, detection loop, result forwarding |
| `requirements.txt` | Python dependencies |
| `model/weapon_yolov10.pt` | Backup/alternative model weights |

### How It Connects
- **Receives start/stop commands** from the Backend via HTTP
- **Opens RTSP stream** directly from the CCTV camera
- **Sends detection results** to the Backend via HTTP POST

---

## 4. ML Model — `Yolov10/`

### What It Is
Contains the **pre-trained YOLOv10 model weights** (`model.pt`) used by the AI service for weapon detection.

### What It Does
- The file `model.pt` is a **PyTorch model checkpoint** containing the trained weights for detecting three weapon classes:
  - **Knife**
  - **Pistol**
  - **Gun**
- The model was trained using the **YOLOv10 architecture** from the Ultralytics library
- It is loaded by `AIService/main.py` at startup

### Note
There is also a copy at `AIService/model/weapon_yolov10.pt`, but the main service loads from `Yolov10/model.pt`.

---

## Complete System Architecture & Data Flow

### System Architecture Diagram
```
┌─────────────────────────────────────────────────────────────────┐
│                        CCTV CAMERA                              │
│                    (RTSP Stream Source)                          │
└──────────────┬──────────────────────────────────────────────────┘
               │ RTSP Stream
               ▼
┌──────────────────────────────┐         ┌──────────────────────┐
│       AIService (Python)     │         │   Yolov10/model.pt   │
│       FastAPI : Port 8000    │◄────────│   (YOLO Weights)     │
│                              │  loads  └──────────────────────┘
│  • OpenCV frame capture      │
│  • YOLOv10 inference         │
│  • Weapon detection          │
│  • Confidence filtering      │
│  • Deduplication             │
└──────────────┬───────────────┘
               │ HTTP POST /api/detections/receive
               │ (weaponType, location, confidence, userId)
               ▼
┌──────────────────────────────────────────────────────────────────┐
│                 Backend (Node.js/Express)                        │
│                 Port 5000                                        │
│                                                                  │
│  ┌──────────────┐  ┌────────────────┐  ┌──────────────────────┐ │
│  │  REST API     │  │  Socket.IO     │  │  MongoDB             │ │
│  │  (Express)    │  │  (WebSocket)   │  │                      │ │
│  │              │  │               │  │  • Users              │ │
│  │  /api/auth    │  │  Events:       │  │  • Authorities       │ │
│  │  /api/dashboard│ │  • weapon-     │  │  • Detections        │ │
│  │  /api/cameras │  │    detected    │  │  • Notifications     │ │
│  │  /api/alerts  │  │  • notification│  │  • Alerts            │ │
│  │  /api/detect  │  │    -created    │  │                      │ │
│  │  /api/notify  │  │  • alert-      │  │                      │ │
│  │  /api/settings│  │    created     │  │                      │ │
│  └──────┬───────┘  └───────┬────────┘  └──────────────────────┘ │
│         │                  │                                     │
└─────────┼──────────────────┼─────────────────────────────────────┘
          │ HTTP             │ WebSocket (Socket.IO)
          │ REST             │ Real-time events
          ▼                  ▼
┌──────────────────────────────────────────────────────────────────┐
│              WeaponDetectionApp (React Native)                   │
│              Mobile Application                                  │
│                                                                  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐ │
│  │  User Screens    │  │ Authority       │  │  Shared          │ │
│  │                  │  │ Screens         │  │                  │ │
│  │  • Dashboard     │  │ • Alert Dash    │  │  • Login         │ │
│  │  • Live Feed     │  │ • Alert Details │  │  • Signup        │ │
│  │  • Notifications │  │ • History       │  │  • Settings      │ │
│  │  • Explore       │  │                 │  │                  │ │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘ │
│                                                                  │
│  Also connects directly to CCTV camera for live RTSP playback   │
└──────────────────────────────────────────────────────────────────┘
```

### End-to-End Flow: Weapon Detection

Here is how the system works from start to finish when a weapon is detected:

#### Step 1: User Starts Detection
1. User opens the mobile app and navigates to **Live Feed**
2. The app connects to the CCTV camera via RTSP and displays live video
3. User taps "Start Detection"
4. The app emits a `start-detection` Socket.IO event to the Backend with:
   - `stream_url` (RTSP URL)
   - `user` (user ID)
   - `location` (camera location)

#### Step 2: Backend Forwards to AI Service
1. Backend receives the `start-detection` Socket.IO event
2. Backend tracks the user-socket association
3. Backend forwards the request via HTTP POST to `AIService:8000/start-detection`
4. Backend emits `detection-started` back to the mobile app

#### Step 3: AI Service Begins Processing
1. AI service loads the YOLOv10 model (if not already loaded)
2. Opens the RTSP stream using OpenCV
3. Starts the `frame_grabber` thread to continuously capture frames
4. Main loop processes the **latest frame** through YOLOv10

#### Step 4: Weapon Detected
1. YOLOv10 detects a weapon (knife/pistol/gun) with confidence ≥ 0.6
2. AI service checks deduplication window (10 seconds)
3. If not a duplicate, sends HTTP POST to `Backend:5000/api/detections/receive` with:
   - `weaponType` (e.g., "pistol")
   - `location` (e.g., "Main Entrance")
   - `confidence` (e.g., 0.85)
   - `userId`

#### Step 5: Backend Processes Detection
1. Backend validates the detection data
2. Saves a `Detection` record in MongoDB
3. Creates a `Notification` for the user (stored in MongoDB)
4. Creates an `Alert` for authorities (stored in MongoDB)
5. Emits three Socket.IO events:
   - `weapon-detected` — real-time alert with detection details
   - `notification-created` — new notification item
   - `alert-created` — new alert for authorities

#### Step 6: Mobile App Shows Alert
1. **User's app**: Receives `weapon-detected` event, shows a red alert banner on the live feed with weapon type and confidence
2. **User's app**: Receives `notification-created` event, updates notification badge/list
3. **Authority's app**: Receives `alert-created` event, shows new alert on their dashboard
4. Authority can **accept**, **dismiss**, or **resolve** the alert

#### Step 7: Detection Stops
- When the user closes the app or disconnects, the Backend detects all sockets are gone and sends a HTTP POST to `AIService:8000/stop-detection`
- AI service stops the frame grabber and detection loop

---

## How to Run the Project

### Prerequisites
- **Node.js** ≥ 18
- **Python** ≥ 3.9
- **MongoDB** (local or cloud)
- **Android Studio** or **Xcode** (for mobile app)
- **CCTV Camera** with RTSP support

### 1. Start MongoDB
```bash
mongod
```

### 2. Start the Backend
```bash
cd Backend
npm install
npm run dev
```
Runs on **http://localhost:5000**

### 3. Start the AI Service
```bash
cd AIService
pip install -r requirements.txt
python main.py
```
Runs on **http://localhost:8000**

### 4. Start the Mobile App
```bash
cd WeaponDetectionApp
npm install

# Android
npx react-native run-android

# iOS
cd ios && pod install && cd ..
npx react-native run-ios
```

### Environment Variables (Backend `.env`)
```env
PORT=5000
MONGO_URI=mongodb://localhost:27017/weapon-detection
JWT_SECRET=your_jwt_secret_key
AI_SERVICE_URL=http://localhost:8000
```

---

## Summary Table

| Aspect | AIService | Backend | WeaponDetectionApp | Yolov10 |
|--------|-----------|---------|-------------------|---------|
| **Language** | Python | JavaScript (Node.js) | TypeScript (React Native) | N/A |
| **Framework** | FastAPI | Express.js | React Native CLI | N/A |
| **Role** | AI/ML Processing | API Server + WebSocket Hub | Mobile Frontend | Model Storage |
| **Port** | 8000 | 5000 | N/A | N/A |
| **Database** | None | MongoDB | AsyncStorage (local) | None |
| **Key Task** | Real-time weapon detection via YOLOv10 on RTSP feeds | Data management, authentication, real-time event distribution | User interface, live feed display, alert reception | Stores trained YOLO model weights |
| **Communicates With** | Backend (sends detections), CCTV (reads RTSP) | AIService, Mobile App, MongoDB | Backend (REST + Socket.IO), CCTV (RTSP playback) | Loaded by AIService |
