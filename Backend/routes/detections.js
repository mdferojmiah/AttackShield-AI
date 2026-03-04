const express = require('express');
const router = express.Router();
const Detection = require('../models/Detection');
const Notification = require('../models/Notification');
const Alert = require('../models/Alert');

// Receive detection from AI service
router.post('/receive', async (req, res) => {
  try {
    const {
      weaponType, location, confidence, imageUrl,
      userId, cameraName, camera_name,
      detectionType = 'weapon',
      cameraId,
      bbox,
    } = req.body;

    if (!weaponType || !location || confidence === undefined) {
      return res.status(400).json({ success: false, error: 'Missing required fields' });
    }

    // Thresholds must match or be lower than the AI service's own confidence
    // thresholds (CONFIDENCE_THRESHOLD=0.25, I3D_CONFIDENCE_THRESHOLD=0.20).
    // Any higher value here silently drops valid detections.
    const thresholds = { weapon: 0.20, suspicious_activity: 0.15, face: 0.35 };
    if (confidence < (thresholds[detectionType] ?? 0.20)) {
      return res.status(200).json({ success: true, message: 'Detection below threshold' });
    }

    const io = req.app.get('io');

    const emitOverlay = (sound) =>
      io.emit('detection-overlay', {
        cameraId, type: detectionType, label: weaponType,
        confidence, bbox: bbox || null, sound,
        timestamp: new Date().toISOString(),
      });

    // Faces: always emit overlay immediately (keeps the box alive on the frontend)
    // then do a lightweight DB save but skip the dedup gate so every detection
    // refreshes the canvas TTL.
    if (detectionType === 'face') {
      emitOverlay(null);
      // Save to DB at most every 30s to avoid flooding
      const recentFace = await Detection.findOne({
        weaponType, location, detectionType: 'face',
        createdAt: { $gte: new Date(Date.now() - 30000) }
      });
      if (!recentFace) {
        const isValidObjId2 = userId && typeof userId === 'string' && /^[a-fA-F0-9]{24}$/.test(userId);
        const det2 = new Detection({
          weaponType, location, confidence, imageUrl,
          cameraName: cameraName || camera_name, detectionType: 'face',
          ...(isValidObjId2 && { userId }),
        });
        await det2.save();
      }
      return res.json({ success: true });
    }

    const dedupSec = { weapon: 10, suspicious_activity: 30 }[detectionType] ?? 10;
    const existing = await Detection.findOne({
      weaponType, location, createdAt: { $gte: new Date(Date.now() - dedupSec * 1000) }
    });
    if (existing) return res.status(200).json({ success: true, message: 'Duplicate ignored' });

    const isValidObjId = userId && typeof userId === 'string' && /^[a-fA-F0-9]{24}$/.test(userId);
    const camName = cameraName || camera_name;

    const det = new Detection({
      weaponType, location, confidence, imageUrl,
      cameraName: camName, detectionType,
      ...(isValidObjId && { userId }),
    });
    await det.save();

    if (detectionType === 'suspicious_activity') {
      const notif = new Notification({
        type: 'suspicious',
        title: `Suspicious Activity: ${weaponType}`,
        description: `Suspicious activity "${weaponType}" detected at ${location}${camName ? ` (Camera: ${camName})` : ''} with ${(confidence * 100).toFixed(1)}% confidence.`,
        location, icon: 'eye',
        ...(isValidObjId && { userId }),
      });
      await notif.save();
      io.emit('notification-created', { type: 'suspicious', title: notif.title, description: notif.description, location, timestamp: notif.createdAt });
      emitOverlay('suspicious');
      return res.json({ success: true, detection: det._id, notification: notif._id });
    }

    const notif = new Notification({
      type: 'weapon',
      title: `Weapon Detected: ${weaponType}`,
      description: `A ${weaponType} was detected at ${location}${camName ? ` (Camera: ${camName})` : ''} with ${(confidence * 100).toFixed(1)}% confidence.`,
      location, icon: 'alert-triangle',
      ...(isValidObjId && { userId }),
    });
    await notif.save();

    const alert = new Alert({
      type: 'high', title: `Weapon Detected: ${weaponType}`,
      message: `Detected at ${location}${camName ? ` (Camera: ${camName})` : ''}`,
      location, imageUrl, detectionId: det._id,
      cameraName: camName, status: 'new',
      ...(isValidObjId && { userId }),
    });
    await alert.save();

    io.emit('weapon-detected',     { weaponType, location, confidence, cameraName: camName, timestamp: det.createdAt });
    io.emit('notification-created',{ type: 'weapon', title: notif.title, description: notif.description, location, timestamp: notif.createdAt });
    io.emit('alert-created',       { id: alert._id, type: alert.type, title: alert.title, message: alert.message, location, cameraName: camName, createdAt: alert.createdAt });
    emitOverlay('weapon');
    return res.json({ success: true, detection: det._id, notification: notif._id, alert: alert._id });

  } catch (error) {
    console.error('Error processing detection:', error);
    res.status(500).json({ success: false, error: 'Internal server error' });
  }
});

module.exports = router;
