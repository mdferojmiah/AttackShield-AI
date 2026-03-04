/**
 * AttackShield AI - Backend Server
 * Main entry point for the API server
 */

require('dotenv').config();
const express = require('express');
const mongoose = require('mongoose');
const cors = require('cors');
const path = require('path');
const helmet = require('helmet');
const rateLimit = require('express-rate-limit');
const http = require('http');
const socketIo = require('socket.io');

// Routes
const authRoutes = require('./routes/auth');
const dashboardRoutes = require('./routes/dashboard');
const notificationsRoutes = require('./routes/notifications');
const alertsRoutes = require('./routes/alerts');
const detectionsRoutes = require('./routes/detections');
const settingsRoutes = require('./routes/settings');
const camerasRoutes = require('./routes/cameras');
const streamRoutes = require('./routes/stream');

// Middleware
const { notFound, errorHandler } = require('./middleware/errorHandler');

// Passport configuration
const passport = require('./config/passport');

const app = express();
const server = http.createServer(app);
const io = socketIo(server, {
  cors: {
    origin: "*",
    methods: ["GET", "POST"]
  }
});
const PORT = process.env.PORT || 5000;
const API_HOST = process.env.API_HOST || '192.168.100.35';

// Security Middleware
app.use(helmet({
  crossOriginResourcePolicy: { policy: "cross-origin" }
}));

// Rate limiting
const limiter = rateLimit({
  windowMs: 15 * 60 * 1000, // 15 minutes
  max: 10000, // generous limit — HLS polling + dashboard refresh
  message: { success: false, error: 'Too many requests, please try again later.' }
});
app.use('/api/', limiter);

// Auth routes rate limiting (stricter)
const authLimiter = rateLimit({
  windowMs: 15 * 60 * 1000,
  max: 20,
  message: { success: false, error: 'Too many authentication attempts, please try again later.' }
});
app.use('/api/auth/login', authLimiter);
app.use('/api/auth/signup', authLimiter);

// CORS Configuration
app.use(cors({
  origin: '*', // In production, specify allowed origins
  methods: ['GET', 'POST', 'PUT', 'DELETE', 'OPTIONS'],
  allowedHeaders: ['Content-Type', 'Authorization']
}));

// Body Parser
app.use(express.json({ limit: '10mb' }));
app.use(express.urlencoded({ extended: true, limit: '10mb' }));

// Passport middleware
app.use(passport.initialize());

// Static files for HLS streams (served BEFORE rate-limited API routes)
// The frontend fetches manifests + segments at high frequency, so this
// must NOT pass through the /api/ rate limiter.
app.use('/streams', express.static(path.join(__dirname, 'public', 'streams'), {
  setHeaders(res, filePath) {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Cache-Control', 'no-cache, no-store, must-revalidate');
    if (filePath.endsWith('.m3u8')) {
      res.setHeader('Content-Type', 'application/vnd.apple.mpegurl');
    } else if (filePath.endsWith('.ts')) {
      res.setHeader('Content-Type', 'video/mp2t');
    } else if (filePath.endsWith('.mp4')) {
      res.setHeader('Content-Type', 'video/mp4');
    } else if (filePath.endsWith('.m4s')) {
      res.setHeader('Content-Type', 'video/iso.segment');
    }
  }
}));

// API Routes
app.use('/api/auth', authRoutes);
app.use('/api/dashboard', dashboardRoutes);
app.use('/api/notifications', notificationsRoutes);
app.use('/api/detections', detectionsRoutes);
app.use('/api/alerts', alertsRoutes);
app.use('/api/settings', settingsRoutes);
app.use('/api/cameras', camerasRoutes);
app.use('/api/stream', streamRoutes);

// Socket.io connection

const axios = require('axios');
const AI_SERVICE_URL = process.env.AI_SERVICE_URL || 'http://localhost:8000';


// Track active sockets by user (by user name or id)
const userSocketMap = new Map();

io.on('connection', (socket) => {
  console.log('User connected:', socket.id);

  // Listen for start-detection from frontend and forward to AI service
  socket.on('start-detection', async (payload) => {
    try {
      // Map payload to AI service expected format.
      // For streams served from our own backend (HLS), use the MJPEG endpoint
      // instead – it is a continuous multipart stream that OpenCV reads natively,
      // whereas HLS uses rolling 0.5-second segments that get deleted before
      // the AI service can consume them.
      let aiStreamUrl = payload.stream_url;
      if (
        payload.camera_id &&
        typeof aiStreamUrl === 'string' &&
        (aiStreamUrl.includes('/streams/') || aiStreamUrl.startsWith('webcam:'))
      ) {
        aiStreamUrl = `http://localhost:${PORT}/api/stream/mjpeg/${payload.camera_id}`;
      }

      const aiPayload = {
        rtsp_url:    aiStreamUrl,
        location:    payload.location,
        user_id:     payload.user,
        camera_name: payload.camera_name,
        camera_id:   payload.camera_id,
      };
      // Track user-socket association
      if (payload.user) {
        if (!userSocketMap.has(payload.user)) userSocketMap.set(payload.user, new Set());
        userSocketMap.get(payload.user).add(socket.id);
        socket.data.user = payload.user;
      }

      // Emit success to the frontend immediately — don't wait on AI model loading.
      // The AI service loads models in the background; detection events will
      // arrive via separate socket emissions once models are ready.
      socket.emit('detection-started', { success: true, message: 'Detection request sent to AI service' });

      // Fire-and-forget: send to AI service without blocking the socket handler.
      // Use a generous timeout (120 s) to cover cold-start model loading.
      console.log('[AI] Forwarding detection request to AI service:', aiPayload);
      axios.post(`${AI_SERVICE_URL}/start-detection`, aiPayload, { timeout: 120000 })
        .then(r  => console.log('[AI] Service response:', r.data?.message || r.data))
        .catch(e => console.error('[AI] Error forwarding to AI service:', e.message));

    } catch (err) {
      console.error('[AI] Error in start-detection handler:', err.message);
      socket.emit('detection-started', { success: false, error: err.message });
    }
  });

  socket.on('disconnect', async () => {
    console.log('User disconnected:', socket.id);
    const user = socket.data.user;
    if (user && userSocketMap.has(user)) {
      userSocketMap.get(user).delete(socket.id);
      if (userSocketMap.get(user).size === 0) {
        userSocketMap.delete(user);
        // All sockets for this user are gone, stop detection
        try {
          console.log(`[Socket] All sockets for user ${user} disconnected. Stopping detection.`);
          await axios.post(`${AI_SERVICE_URL}/stop-detection`, {}, { timeout: 5000 });
        } catch (err) {
          console.error('[AI] Error stopping detection in AI service:', err.message);
        }
      }
    }
  });
});

// Make io available in routes
app.set('io', io);

// Health check endpoint
app.get('/api/health', (req, res) => {
  res.json({ 
    success: true, 
    status: 'healthy',
    timestamp: new Date().toISOString(),
    uptime: process.uptime()
  });
});

// Error Handling
app.use(notFound);
app.use(errorHandler);

// Database Connection & Server Start
const startServer = async () => {
  try {
    // MongoDB Connection with retry logic
    const mongoOptions = {
      useNewUrlParser: true,
      useUnifiedTopology: true,
      serverSelectionTimeoutMS: 5000,
      socketTimeoutMS: 45000,
    };

    await mongoose.connect(
      process.env.MONGO_URI || 'mongodb://localhost:27017/weapon-detection',
      mongoOptions
    );
    
    console.log('[DB] Connected to MongoDB');

    // Start server
    server.listen(PORT, '0.0.0.0', () => {
      console.log(`[Server] Listening on port ${PORT}`);
      console.log(`[Server] Accessible at http://localhost:${PORT}`);
      console.log(`[Server] Accessible at http://192.168.100.35:${PORT}`);
      console.log(`[Server] Accessible at http://0.0.0.0:${PORT}`);
      console.log(`[Server] Environment: ${process.env.NODE_ENV || 'development'}`);
      
      // Database connected successfully
    }).on('error', (err) => {
      console.error('[Server] Failed to start:', err.message);
      process.exit(1);
    });
  } catch (error) {
    console.error('[Server] Failed to start server:', error.message);
    process.exit(1);
  }
};

// Handle unhandled promise rejections
process.on('unhandledRejection', (err) => {
  console.error('Unhandled Rejection:', err.message);
  // Close server & exit
  process.exit(1);
});

// Graceful shutdown
const { cleanupAllStreams } = require('./controllers/streamController');
process.on('SIGTERM', () => {
  console.log('SIGTERM received. Shutting down gracefully...');
  cleanupAllStreams();
  mongoose.connection.close(false, () => {
    console.log('MongoDB connection closed.');
    process.exit(0);
  });
});
process.on('SIGINT', () => {
  console.log('SIGINT received. Cleaning up streams...');
  cleanupAllStreams();
  process.exit(0);
});

// Start the server
startServer();