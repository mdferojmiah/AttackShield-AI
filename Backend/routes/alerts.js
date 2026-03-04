/**
 * Alerts Routes
 */

const express = require('express');
const router = express.Router();

const { authenticate, authorize } = require('../middleware/auth');
const {
  getNewAlerts,
  getMyActiveAlerts,
  getMyHistoryAlerts,
  acceptAlert,
  dismissAlert,
  resolveAlert,
} = require('../controllers/alertsController');

// All routes require authority authentication
router.use(authenticate, authorize('authority', 'senior_authority', 'admin'));

router.get('/new', getNewAlerts);
router.get('/my-active', getMyActiveAlerts);
router.get('/history', getMyHistoryAlerts);
router.post('/:id/accept', acceptAlert);
router.post('/:id/dismiss', dismissAlert);
router.post('/:id/resolve', resolveAlert);

module.exports = router;
