/**
 * Cameras Routes
 * Manage additional cameras per user
 */

const express = require('express');
const router = express.Router();

const { getCameras, addCamera, deleteCamera } = require('../controllers/camerasController');
const { authenticate } = require('../middleware/auth');

// Protected routes
router.get('/', authenticate, getCameras);
router.post('/', authenticate, addCamera);
router.delete('/:id', authenticate, deleteCamera);

module.exports = router;
