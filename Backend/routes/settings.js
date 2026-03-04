/**
 * Settings Routes
 */

const express = require('express');
const router = express.Router();
const { authenticate } = require('../middleware/auth');
const { getSettings, updateSettings } = require('../controllers/settingsController');

// Authenticated routes for user settings
router.get('/', authenticate, getSettings);
router.put('/', authenticate, updateSettings);

module.exports = router;
