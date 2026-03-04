/**
 * Cameras Controller
 * Manage additional cameras per user
 */

const User = require('../models/User');
const { generateRtspUrlFromCamera } = require('./authController');

/**
 * @desc    Get all cameras for current user (including primary as first entry)
 * @route   GET /api/cameras
 * @access  Private
 */
const getCameras = async (req, res, next) => {
  try {
    const user = await User.findById(req.user._id);
    if (!user) {
      return res.status(404).json({ success: false, error: 'User not found' });
    }

    const primaryCamera = {
      id: 'primary',
      name: user.cctvName,
      rtspUrl: user.rtspUrl,
      location: user.location,
      brand: undefined,
    };

    const extraCameras = (user.cameras || []).map((cam, index) => ({
      id: cam._id?.toString() || `extra-${index}`,
      name: cam.name,
      rtspUrl: cam.rtspUrl,
      location: cam.location,
      brand: cam.brand,
    }));

    res.json({
      success: true,
      data: [primaryCamera, ...extraCameras],
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Add a new camera for current user
 * @route   POST /api/cameras
 * @access  Private
 */
const addCamera = async (req, res, next) => {
  try {
    const { name, location, rtspUrl, cameraIp, cameraUsername, cameraPassword, cameraPort, cameraBrand, cameraPath } = req.body;

    if (!name || !location) {
      return res.status(400).json({ success: false, error: 'Camera name and location are required' });
    }

    let finalRtspUrl = rtspUrl;

    if (!finalRtspUrl) {
      finalRtspUrl = generateRtspUrlFromCamera({
        ip: cameraIp,
        username: cameraUsername,
        password: cameraPassword,
        port: cameraPort,
        brand: cameraBrand,
        path: cameraPath,
      });
    }

    if (!finalRtspUrl) {
      return res.status(400).json({ success: false, error: 'Unable to generate RTSP URL from provided camera details' });
    }

    const user = await User.findById(req.user._id);
    if (!user) {
      return res.status(404).json({ success: false, error: 'User not found' });
    }

    user.cameras = user.cameras || [];
    user.cameras.push({
      name,
      rtspUrl: finalRtspUrl,
      location,
      brand: cameraBrand,
    });

    await user.save();

    res.status(201).json({ success: true, message: 'Camera added successfully' });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Delete a camera for current user
 * @route   DELETE /api/cameras/:id
 * @access  Private
 */
const deleteCamera = async (req, res, next) => {
  try {
    const { id } = req.params;
    const user = await User.findById(req.user._id);
    if (!user) {
      return res.status(404).json({ success: false, error: 'User not found' });
    }

    if (id === 'primary') {
      // Clear primary camera fields
      user.rtspUrl = '';
      user.cctvName = '';
      await user.save();
      return res.json({ success: true, message: 'Primary camera removed' });
    }

    // Remove from cameras array
    const camIndex = (user.cameras || []).findIndex(
      (cam) => cam._id?.toString() === id
    );
    if (camIndex === -1) {
      return res.status(404).json({ success: false, error: 'Camera not found' });
    }

    user.cameras.splice(camIndex, 1);
    await user.save();

    res.json({ success: true, message: 'Camera removed successfully' });
  } catch (error) {
    next(error);
  }
};

module.exports = {
  getCameras,
  addCamera,
  deleteCamera,
};
