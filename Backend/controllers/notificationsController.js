/**
 * Notifications Controller
 * Handles notification operations
 */


const Notification = require('../models/Notification');

/**
 * @desc    Get all notifications
 * @route   GET /api/notifications
 * @access  Private
 */
const getNotifications = async (req, res, next) => {
  try {
    // Debug: Log headers and user info
    console.log('--- NOTIFICATIONS DEBUG ---');
    console.log('Headers:', req.headers);
    console.log('User:', req.user);

    // Fetch all notifications from MongoDB, newest first
    const notifications = await Notification.find({})
      .sort({ createdAt: -1 })
      .lean();
    // Map MongoDB _id to id and createdAt to time for frontend compatibility
    const mapped = notifications.map(n => ({
      ...n,
      id: n._id?.toString(),
      time: n.createdAt ? new Date(n.createdAt).toLocaleString() : '',
    }));
    console.log('Notifications returned:', mapped.length);
    res.json(mapped);
  } catch (error) {
    console.error('Error in getNotifications:', error);
    next(error);
  }
};

/**
 * @desc    Get single notification
 * @route   GET /api/notifications/:id
 * @access  Private
 */
const getNotification = async (req, res, next) => {
  try {
    const notification = notifications.find(n => n.id === req.params.id);
    
    if (!notification) {
      return res.status(404).json({
        success: false,
        error: 'Notification not found'
      });
    }

    res.json({
      success: true,
      data: notification
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Create notification (from system/ML model)
 * @route   POST /api/notifications
 * @access  Private
 */
const createNotification = async (req, res, next) => {
  try {
    const { type, title, description, location, userId } = req.body;

    const iconMap = {
      weapon: 'alert-circle',
      suspicious: 'warning',
      vehicle: 'car',
      loitering: 'person',
      package: 'cube',
      camera: 'videocam-off',
      system: 'settings'
    };

    const notification = {
      id: Date.now().toString(),
      type: type || 'system',
      title,
      description,
      time: new Date().toLocaleTimeString(),
      icon: iconMap[type] || 'notifications',
      isRead: false,
      location,
      userId,
      createdAt: new Date().toISOString()
    };

    notifications.unshift(notification);

    // Keep only last 100 notifications
    if (notifications.length > 100) {
      notifications = notifications.slice(0, 100);
    }

    res.status(201).json({
      success: true,
      data: notification
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Mark notification as read
 * @route   PUT /api/notifications/:id/read
 * @access  Private
 */
const markAsRead = async (req, res, next) => {
  try {
    const notification = notifications.find(n => n.id === req.params.id);
    
    if (!notification) {
      return res.status(404).json({
        success: false,
        error: 'Notification not found'
      });
    }

    notification.isRead = true;

    res.json({
      success: true,
      message: 'Notification marked as read',
      data: notification
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Mark all notifications as read
 * @route   PUT /api/notifications/read-all
 * @access  Private
 */
const markAllAsRead = async (req, res, next) => {
  try {
    const userId = req.user?._id?.toString();
    
    notifications.forEach(n => {
      if (!n.userId || n.userId === userId) {
        n.isRead = true;
      }
    });

    res.json({
      success: true,
      message: 'All notifications marked as read'
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Delete notification
 * @route   DELETE /api/notifications/:id
 * @access  Private
 */
const deleteNotification = async (req, res, next) => {
  try {
    const index = notifications.findIndex(n => n.id === req.params.id);
    
    if (index === -1) {
      return res.status(404).json({
        success: false,
        error: 'Notification not found'
      });
    }

    notifications.splice(index, 1);

    res.json({
      success: true,
      message: 'Notification deleted'
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Clear all notifications
 * @route   DELETE /api/notifications/clear
 * @access  Private
 */
const clearAll = async (req, res, next) => {
  try {
    const userId = req.user?._id?.toString();
    
    notifications = notifications.filter(n => n.userId && n.userId !== userId);

    res.json({
      success: true,
      message: 'All notifications cleared'
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Get unread count
 * @route   GET /api/notifications/unread-count
 * @access  Private
 */
const getUnreadCount = async (req, res, next) => {
  try {
    const userId = req.user?._id?.toString();
    
    const count = notifications.filter(n => 
      (!n.userId || n.userId === userId) && !n.isRead
    ).length;

    res.json({
      success: true,
      count
    });
  } catch (error) {
    next(error);
  }
};

module.exports = {
  getNotifications,
  getNotification,
  createNotification,
  markAsRead,
  markAllAsRead,
  deleteNotification,
  clearAll,
  getUnreadCount
};
