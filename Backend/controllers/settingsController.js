/**
 * Settings Controller
 * Manage user application preferences and settings
 */

const User = require('../models/User');

/**
 * Helper: convert nested settings to the flat convenience keys the frontend uses.
 */
function toFlat(s) {
  if (!s) return {};
  const n = s.notifications || {};
  const d = s.detection || {};
  const a = s.app || {};
  return {
    notifications: s.notifications,
    detection: s.detection,
    app: s.app,
    notificationsEnabled: n.push ?? true,
    soundEnabled: n.sound ?? true,
    vibrationEnabled: n.vibration ?? true,
    detectionSensitivity: d.sensitivity ?? 'medium',
    alertThreshold: d.alertThreshold ?? 5,
    darkMode: (a.theme ?? 'dark') === 'dark',
    autoStartMonitoring: d.autoStartMonitoring ?? false,
  };
}

// Get current user's settings
exports.getSettings = async (req, res) => {
  try {
    const user = await User.findById(req.user._id).select('settings');
    if (!user) {
      return res.status(404).json({ success: false, error: 'User not found' });
    }
    return res.json({ success: true, data: toFlat(user.settings) });
  } catch (error) {
    return res.status(500).json({ success: false, error: 'Failed to fetch settings' });
  }
};

// Update current user's settings (partial updates supported)
exports.updateSettings = async (req, res) => {
  try {
    const updates = req.body?.settings || req.body || {};
    const user = await User.findById(req.user._id);
    if (!user) {
      return res.status(404).json({ success: false, error: 'User not found' });
    }

    // Ensure nested settings objects exist
    if (!user.settings) user.settings = {};
    if (!user.settings.notifications) user.settings.notifications = { push: true, sound: true, vibration: true };
    if (!user.settings.detection) user.settings.detection = { sensitivity: 'medium', alertThreshold: 5 };
    if (!user.settings.app) user.settings.app = { theme: 'dark' };

    // Shallow merge nested groups when provided directly
    const mergeGroup = (group) => {
      if (updates[group] && typeof updates[group] === 'object') {
        user.settings[group] = { ...(user.settings[group] || {}), ...updates[group] };
      }
    };
    mergeGroup('notifications');
    mergeGroup('detection');
    mergeGroup('app');

    // Map flat convenience keys → nested structure
    if ('notificationsEnabled' in updates) {
      user.settings.notifications.push = updates.notificationsEnabled;
    }
    if ('soundEnabled' in updates) {
      user.settings.notifications.sound = updates.soundEnabled;
    }
    if ('vibrationEnabled' in updates) {
      user.settings.notifications.vibration = updates.vibrationEnabled;
    }
    if ('detectionSensitivity' in updates) {
      user.settings.detection.sensitivity = updates.detectionSensitivity;
    }
    if ('alertThreshold' in updates) {
      user.settings.detection.alertThreshold = updates.alertThreshold;
    }
    if ('darkMode' in updates) {
      user.settings.app.theme = updates.darkMode ? 'dark' : 'light';
    }
    if ('autoStartMonitoring' in updates) {
      user.settings.detection.autoStartMonitoring = updates.autoStartMonitoring;
    }

    // Mark nested field as modified so Mongoose saves it
    user.markModified('settings');
    await user.save();

    return res.json({ success: true, data: toFlat(user.settings) });
  } catch (error) {
    return res.status(500).json({ success: false, error: 'Failed to update settings' });
  }
};
