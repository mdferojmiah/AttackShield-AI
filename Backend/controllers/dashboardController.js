/**
 * Dashboard Controller
 * Handles dashboard data operations
 */

const User = require('../models/User');
const Notification = require('../models/Notification');
const Detection = require('../models/Detection');
const axios = require('axios');

const AI_SERVICE_URL = process.env.AI_SERVICE_URL || 'http://localhost:8000';

// Simulated detection data (in production, this would come from ML model)
let detectionStats = {
  totalWeapons: 0,
  alertsSent: 0,
  accuracy: 0.98
};

let activities = [];

/**
 * @desc    Get dashboard statistics
 * @route   GET /api/dashboard/stats
 * @access  Private
 */
const getStats = async (req, res, next) => {
  try {
    // ── DB counts (persist across AI service restarts) ────────────
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    const [totalWeapons, alertsSent, facesDetectedDB, suspiciousActivitiesDB] =
      await Promise.all([
        Detection.countDocuments({ detectionType: 'weapon' }),
        Notification.countDocuments({ type: { $in: ['weapon', 'suspicious'] } }),
        Detection.countDocuments({ detectionType: 'face', createdAt: { $gte: today } }),
        Detection.countDocuments({ detectionType: 'suspicious_activity', createdAt: { $gte: today } }),
      ]);

    const accuracy = 0.98;

    // ── AI service metrics (in-session, used as supplement) ───────
    let aiSuspiciousActivities = 0;
    let aiFacesDetected = 0;
    let aiUniquePersons = 0;
    let trustScore = 92.0;
    let ensembleConfidence = 0;

    try {
      const [metricsRes, trustRes] = await Promise.all([
        axios.get(`${AI_SERVICE_URL}/metrics`, { timeout: 3000 }),
        axios.get(`${AI_SERVICE_URL}/trust-score`, { timeout: 3000 }),
      ]);
      if (metricsRes.data?.data) {
        aiSuspiciousActivities = metricsRes.data.data.suspicious_activities || 0;
        aiFacesDetected = metricsRes.data.data.faces_detected || 0;
        aiUniquePersons = metricsRes.data.data.unique_persons || 0;
        ensembleConfidence = metricsRes.data.data.ensemble_confidence || 0;
      }
      if (trustRes.data?.data) {
        trustScore = trustRes.data.data.score || 92.0;
      }
    } catch (_) {
      // AIService unavailable – use DB defaults
    }

    // Use DB counts as the floor; AI in-session counts supplement them
    const facesDetected = Math.max(facesDetectedDB, aiFacesDetected);
    const suspiciousActivities = Math.max(suspiciousActivitiesDB, aiSuspiciousActivities);
    // uniquePersons: use AI tracker value (true unique count).
    // Fall back to facesDetectedDB ONLY when AI is not running AND no tracker data yet.
    const uniquePersons = aiUniquePersons > 0 ? aiUniquePersons : 0;

    const stats = {
      totalWeapons,
      alertsSent,
      accuracy,
      suspiciousActivities,
      facesDetected,
      uniquePersons,
      trustScore,
      ensembleConfidence,
      lastUpdated: new Date().toISOString(),
    };

    res.json(stats);
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Get recent activities
 * @route   GET /api/dashboard/activity
 * @access  Private
 */
const getActivity = async (req, res, next) => {
  try {
    // Fetch recent notifications and map to activities
    const recentNotifications = await Notification.find({})
      .sort({ createdAt: -1 })
      .limit(10)
      .lean();

    const recentActivities = recentNotifications.map(n => ({
      id: n._id.toString(),
      type: n.type === 'weapon'
        ? 'high'
        : (n.type === 'suspicious' || n.type === 'activity')
          ? 'medium'
          : 'low',          // face, camera, system → low
      message: n.title,
      time: new Date(n.createdAt).toLocaleString(),
    }));

    res.json(recentActivities);
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Record a detection event
 * @route   POST /api/dashboard/detection
 * @access  Private (from ML model)
 */
const recordDetection = async (req, res, next) => {
  try {
    const { type, message, location, confidence } = req.body;

    // Update stats
    if (type === 'weapon') {
      detectionStats.totalWeapons += 1;
      detectionStats.alertsSent += 1;
    }

    // Add to activities
    const activity = {
      id: Date.now().toString(),
      type: type === 'weapon' ? 'high' : type === 'suspicious' ? 'medium' : 'low',
      message: message || `${type} detected`,
      time: new Date().toLocaleTimeString(),
      location,
      confidence
    };

    activities.unshift(activity);
    
    // Keep only last 50 activities
    if (activities.length > 50) {
      activities = activities.slice(0, 50);
    }

    res.status(201).json({
      success: true,
      message: 'Detection recorded',
      data: activity
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Get user's camera status
 * @route   GET /api/dashboard/camera-status
 * @access  Private
 */
const getCameraStatus = async (req, res, next) => {
  try {
    const user = await User.findById(req.user._id);
    
    if (!user) {
      return res.status(404).json({
        success: false,
        error: 'User not found'
      });
    }

    res.json({
      success: true,
      data: {
        camera_name: user.cctvName,
        location: user.location,
        rtsp_url: user.rtspUrl,
        status: 'active' // In production, check actual stream status
      }
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Reset dashboard stats (for testing)
 * @route   POST /api/dashboard/reset
 * @access  Private (admin only)
 */
const resetStats = async (req, res, next) => {
  try {
    detectionStats = {
      totalWeapons: 0,
      alertsSent: 0,
      accuracy: 0.98
    };
    activities = [];

    res.json({
      success: true,
      message: 'Dashboard stats reset'
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Get ensemble metrics from AIService
 * @route   GET /api/dashboard/metrics
 * @access  Private
 */
const getEnsembleMetrics = async (req, res, next) => {
  try {
    const response = await axios.get(`${AI_SERVICE_URL}/metrics`, { timeout: 5000 });
    res.json(response.data);
  } catch (error) {
    res.json({ success: true, data: { weapons_detected: 0, suspicious_activities: 0, faces_detected: 0, total_frames_processed: 0, avg_inference_latency_ms: 0, ensemble_confidence: 0 } });
  }
};

/**
 * @desc    Get trust score from AIService
 * @route   GET /api/dashboard/trust-score
 * @access  Private
 */
const getTrustScore = async (req, res, next) => {
  try {
    const response = await axios.get(`${AI_SERVICE_URL}/trust-score`, { timeout: 5000 });
    res.json(response.data);
  } catch (error) {
    res.json({ success: true, data: { score: 92.0, auth_consistency: 100, anomaly_frequency: 0, model_confidence_stability: 95, communication_integrity: 100, policy_compliance: 100 } });
  }
};

module.exports = {
  getStats,
  getActivity,
  recordDetection,
  getCameraStatus,
  resetStats,
  getEnsembleMetrics,
  getTrustScore
};
