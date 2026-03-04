/**
 * Notifications Routes
 * Handles notification endpoints
 */

const express = require('express');
const router = express.Router();

const {
  getNotifications,
  getNotification,
  createNotification,
  markAsRead,
  markAllAsRead,
  deleteNotification,
  clearAll,
  getUnreadCount
} = require('../controllers/notificationsController');

const { authenticate, optionalAuth } = require('../middleware/auth');

// Get all notifications (optional auth for user-specific)
router.get('/', optionalAuth, getNotifications);

// Get unread count
router.get('/unread-count', optionalAuth, getUnreadCount);

// Mark all as read
router.put('/read-all', optionalAuth, markAllAsRead);

// Clear all notifications
router.delete('/clear', optionalAuth, clearAll);

// Get single notification
router.get('/:id', optionalAuth, getNotification);

// Create notification (from system)
router.post('/', authenticate, createNotification);

// Mark single as read
router.put('/:id/read', optionalAuth, markAsRead);

// Delete notification
router.delete('/:id', optionalAuth, deleteNotification);

module.exports = router;
