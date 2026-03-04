# AttackShield AI – Intelligent Surveillance System

A web application built using React + Vite + TypeScript, designed to transform ordinary CCTV cameras into an intelligent, real-time security and threat-detection system. It leverages a **multi-model ensemble architecture**: YOLOv10 for weapon detection, motion-based spatiotemporal analysis for suspicious activity detection, and Haar/RetinaFace for face detection. The system delivers live monitoring, automated alerts, trust-score governance, ensemble metrics, and a complete security workflow for users and authorities.

🚀 Features Completed So Far
✔ 1. User Authentication

User Signup with full details

Authority/Police Signup with station mapping

Unified Login screen (User + Authority + Admin)

Secure backend integration via Node.js + MongoDB

✔ 2. CCTV Integration

During signup, users can attach their CCTV details:

Camera Name

RTSP/HTTP Stream URL

Camera Location

Linked to user profile in MongoDB
After login, CCTV automatically loads into the Live Feed screen.

✔ 3. Live Feed Screen

A dedicated screen where users can see:

Real-time CCTV video stream

Overlay controls like Zoom, Brightness, Settings

Detection banners (future): Weapon Detected, Suspicious Activity, Person of Interest

Fully designed UI, matching modern dark-theme security dashboards.

✔ 4. Dashboard Screen

Displays user’s system summary:

Total detected weapons

Total alerts sent

Accuracy (placeholder)

Recent Activity List: Critical, Medium, Normal events

Built in clean, card-based layout with dark UI and accent colors.

✔ 5. Notifications Screen

List-style interface showing real-time events such as:

Suspicious Activity Detected

Unusual Movement

Person Loitering Near Entrance

Package Left Unattended

Camera Offline
Includes icons, timestamps, and short descriptions.

📁 Project Structure (Frontend Only So Far)
project/
 ├── app/
 │   ├── screens/
 │   │   ├── LoginScreen.tsx
 │   │   ├── UserSignupScreen.tsx
 │   │   ├── AuthoritySignup.tsx
 │   │   ├── LiveFeedScreen.tsx
 │   │   ├── DashboardScreen.tsx
 │   │   ├── NotificationsScreen.tsx
 │   ├── components/
 │   ├── navigation/
 │   ├── utils/
 ├── package.json
 └── README.md

🛠 Tech Stack
Frontend (Mobile App)

React Native + Expo

React Navigation

Axios

Modern UI + Dark Theme

Backend (Partially Connected)

Node.js + Express

MongoDB + Mongoose

JWT Authentication

📅 Next Steps (Upcoming Implementation)

Weapon detection overlay using stream frames

Suspicious behavior classification

Face recognition module

Police authority dashboard

Alert dispatch workflow
