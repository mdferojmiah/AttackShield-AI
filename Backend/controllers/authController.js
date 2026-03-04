/**
 * Authentication Controller
 * Handles all authentication-related operations
 */

const jwt = require('jsonwebtoken');
const crypto = require('crypto');
const User = require('../models/User');
const Authority = require('../models/Authority');

// API Configuration
const API_HOST = process.env.API_HOST || '192.168.100.35';
const API_PORT = process.env.API_PORT || '5000';
const API_BASE = `http://${API_HOST}:${API_PORT}`;

// Generate JWT Token
const generateToken = (id, role) => {
  return jwt.sign(
    { id, role },
    process.env.JWT_SECRET,
    { expiresIn: process.env.JWT_EXPIRE || '7d' }
  );
};

// Store reset tokens (use Redis in production)
const resetTokens = new Map();

/**
 * Helper: Generate RTSP URL from camera details
 * Keeps DB schema the same while making signup user-friendly.
 */
const generateRtspUrlFromCamera = ({
  ip,
  username,
  password,
  port = 554,
  brand,
  path
}) => {
  if (!ip) return null;

  const normalizedBrand = (brand || '').toLowerCase();
  let streamPath = path;

  if (!streamPath) {
    if (normalizedBrand.includes('hikvision')) {
      streamPath = '/Streaming/Channels/101';
    } else if (normalizedBrand.includes('dahua')) {
      streamPath = '/cam/realmonitor?channel=1&subtype=1';
    } else if (normalizedBrand.includes('meari')) {
      streamPath = '/live';
    } else {
      streamPath = '/cam/realmonitor?channel=1&subtype=1';
    }
  }

  // Meari cameras typically use port 8554 for RTSP
  let finalPort = port || 554;
  if (normalizedBrand.includes('meari') && !port) {
    finalPort = 8554;
  }

  // Build RTSP URL — credentials are optional
  if (username && password) {
    return `rtsp://${encodeURIComponent(username)}:${encodeURIComponent(password)}@${ip}:${finalPort}${streamPath}`;
  }
  return `rtsp://${ip}:${finalPort}${streamPath}`;
};

/**
 * @desc    Register a new user
 * @route   POST /api/auth/signup/user
 * @access  Public
 */
const registerUser = async (req, res, next) => {
  try {
    const {
      name,
      email,
      phone,
      password,
      cctvName,
      rtspUrl,
      location,
      // New camera-friendly fields (used only to generate RTSP)
      cameraIp,
      cameraUsername,
      cameraPassword,
      cameraPort,
      cameraBrand,
      cameraPath,
    } = req.body;

    // Check if user exists
    const existingUser = await User.findOne({ email: email.toLowerCase() });
    if (existingUser) {
      return res.status(400).json({
        success: false,
        error: 'User with this email already exists'
      });
    }

    // Determine final RTSP URL.
    // Priority: explicit rtspUrl (for backward compatibility),
    // otherwise generate from camera fields.
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
      return res.status(400).json({
        success: false,
        error: 'Unable to generate RTSP URL from provided camera details',
      });
    }

    // Create user (password hashing handled by model)
    const user = await User.create({
      name,
      email: email.toLowerCase(),
      phone,
      password,
      cctvName,
      rtspUrl: finalRtspUrl,
      location
    });

    res.status(201).json({
      success: true,
      message: 'User registered successfully',
      data: {
        id: user._id,
        name: user.name,
        email: user.email
      }
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Register a new authority
 * @route   POST /api/auth/signup/authority
 * @access  Public
 */
const registerAuthority = async (req, res, next) => {
  try {
    const { name, email, officerId, stationName, password } = req.body;

    // Check if authority exists
    const existingAuthority = await Authority.findOne({ 
      $or: [
        { email: email.toLowerCase() },
        { officerId: officerId.toUpperCase() }
      ]
    });
    
    if (existingAuthority) {
      return res.status(400).json({
        success: false,
        error: 'Authority with this email or officer ID already exists'
      });
    }

    // Create authority
    const authority = await Authority.create({
      name,
      email: email.toLowerCase(),
      officerId: officerId.toUpperCase(),
      stationName,
      password
    });

    res.status(201).json({
      success: true,
      message: 'Authority registered successfully',
      data: {
        id: authority._id,
        name: authority.name,
        email: authority.email,
        officerId: authority.officerId
      }
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Login user or authority
 * @route   POST /api/auth/login
 * @access  Public
 */
const login = async (req, res, next) => {
  try {
    const { email, password } = req.body;
    const emailLower = email.toLowerCase();

    // Try to find user first
    let user = await User.findOne({ email: emailLower }).select('+password');
    let role = 'user';
    let camera = null;

    if (!user) {
      // Try to find authority
      user = await Authority.findOne({ email: emailLower }).select('+password');
      role = user?.role || 'authority';
    } else {
      // Build camera info for user
      camera = {
        camera_name: user.cctvName,
        stream_url: `${API_BASE}/streams/stream.m3u8`,
        location: user.location,
        rtsp_url: user.rtspUrl
      };
    }

    if (!user) {
      return res.status(401).json({
        success: false,
        error: 'Invalid email or password'
      });
    }

    if (!user.isActive) {
      return res.status(401).json({
        success: false,
        error: 'Account is deactivated. Please contact support.'
      });
    }

    // Check password
    const isMatch = await user.comparePassword(password);
    if (!isMatch) {
      return res.status(401).json({
        success: false,
        error: 'Invalid email or password'
      });
    }

    // Update last login
    user.lastLogin = new Date();
    await user.save({ validateBeforeSave: false });

    // Generate token
    const token = generateToken(user._id, role);

    // Build response
    const userData = {
      id: user._id,
      _id: user._id,
      name: user.name,
      email: user.email,
      role,
      ...(camera && { camera }),
      ...(role === 'user' && {
        phone: user.phone,
        cctvName: user.cctvName,
        location: user.location
      }),
      ...(role !== 'user' && {
        officerId: user.officerId,
        stationName: user.stationName,
        department: user.department,
        isVerified: user.isVerified
      })
    };

    res.json({
      success: true,
      token,
      user: userData,
      role
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Get current user profile
 * @route   GET /api/auth/me
 * @access  Private
 */
const getProfile = async (req, res, next) => {
  try {
    res.json({
      success: true,
      data: req.user
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Update user profile
 * @route   PUT /api/auth/profile
 * @access  Private
 */
const updateProfile = async (req, res, next) => {
  try {
    const allowedUpdates = ['name', 'phone', 'cctvName', 'rtspUrl', 'location'];
    const updates = {};

    allowedUpdates.forEach(field => {
      if (req.body[field] !== undefined) {
        updates[field] = req.body[field];
      }
    });

    const user = await User.findByIdAndUpdate(
      req.user._id,
      updates,
      { new: true, runValidators: true }
    );

    res.json({
      success: true,
      message: 'Profile updated successfully',
      data: user
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Change password
 * @route   PUT /api/auth/change-password
 * @access  Private
 */
const changePassword = async (req, res, next) => {
  try {
    const { currentPassword, newPassword } = req.body;

    // Get user with password
    const user = await User.findById(req.user._id).select('+password') ||
                 await Authority.findById(req.user._id).select('+password');

    if (!user) {
      return res.status(404).json({
        success: false,
        error: 'User not found'
      });
    }

    // Check current password
    const isMatch = await user.comparePassword(currentPassword);
    if (!isMatch) {
      return res.status(400).json({
        success: false,
        error: 'Current password is incorrect'
      });
    }

    // Update password
    user.password = newPassword;
    await user.save();

    res.json({
      success: true,
      message: 'Password changed successfully'
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Forgot password - request reset
 * @route   POST /api/auth/forgot-password
 * @access  Public
 */
const forgotPassword = async (req, res, next) => {
  try {
    const { email } = req.body;
    const emailLower = email.toLowerCase();

    // Find user or authority
    let user = await User.findOne({ email: emailLower });
    let userType = 'user';
    
    if (!user) {
      user = await Authority.findOne({ email: emailLower });
      userType = 'authority';
    }

    // Always return success to prevent email enumeration
    if (!user) {
      return res.json({
        success: true,
        message: 'If an account exists with this email, a reset link has been sent.'
      });
    }

    // Generate reset token
    const resetToken = crypto.randomBytes(32).toString('hex');
    const resetTokenExpiry = Date.now() + 30 * 60 * 1000; // 30 minutes

    // Store token
    resetTokens.set(resetToken, {
      email: emailLower,
      userType,
      expiry: resetTokenExpiry
    });

    // Log reset link (in production, send email)
    console.log(`Password reset token for ${email}: ${resetToken}`);
    console.log(`Reset link: ${API_BASE}/api/auth/reset-password/${resetToken}`);

    res.json({
      success: true,
      message: 'Password reset link has been sent to your email',
      ...(process.env.NODE_ENV !== 'production' && { resetToken })
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Reset password with token
 * @route   POST /api/auth/reset-password/:token
 * @access  Public
 */
const resetPassword = async (req, res, next) => {
  try {
    const { token } = req.params;
    const { password } = req.body;

    // Validate token
    const tokenData = resetTokens.get(token);
    if (!tokenData) {
      return res.status(400).json({
        success: false,
        error: 'Invalid or expired reset token'
      });
    }

    // Check expiry
    if (Date.now() > tokenData.expiry) {
      resetTokens.delete(token);
      return res.status(400).json({
        success: false,
        error: 'Reset token has expired'
      });
    }

    // Find and update user
    let user;
    if (tokenData.userType === 'authority') {
      user = await Authority.findOne({ email: tokenData.email });
    } else {
      user = await User.findOne({ email: tokenData.email });
    }

    if (!user) {
      return res.status(400).json({
        success: false,
        error: 'User not found'
      });
    }

    // Update password
    user.password = password;
    await user.save();

    // Delete used token
    resetTokens.delete(token);

    res.json({
      success: true,
      message: 'Password reset successfully'
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Logout user (client-side token deletion)
 * @route   POST /api/auth/logout
 * @access  Private
 */
const logout = async (req, res, next) => {
  try {
    // Stop AIService detection for this user session
    const axios = require('axios');
    const AI_SERVICE_URL = process.env.AI_SERVICE_URL || 'http://localhost:8000';
    try {
      await axios.post(`${AI_SERVICE_URL}/stop-detection`, {}, { timeout: 5000 });
    } catch (err) {
      // Log but don't block logout if AIService is unreachable
      console.error('Failed to notify AIService to stop detection:', err.message);
    }
    res.json({
      success: true,
      message: 'Logged out successfully and AIService stopped.'
    });
  } catch (error) {
    next(error);
  }
};

/**
 * @desc    Google OAuth callback handler
 * @route   GET /api/auth/google/callback
 * @access  Public
 */
const googleCallback = async (req, res) => {
  try {
    const user = req.user;
    const token = generateToken(user._id, user.role);

    // Update last login
    user.lastLogin = new Date();
    await user.save({ validateBeforeSave: false });

    // Build user data for the frontend
    const userData = {
      _id: user._id,
      name: user.name,
      email: user.email,
      role: user.role,
      avatar: user.avatar,
      cctvName: user.cctvName,
      rtspUrl: user.rtspUrl,
      location: user.location,
    };

    const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:3000';
    const params = new URLSearchParams({
      token,
      user: JSON.stringify(userData),
    });
    res.redirect(`${FRONTEND_URL}/login?${params.toString()}`);
  } catch (error) {
    console.error('Google callback error:', error);
    const FRONTEND_URL = process.env.FRONTEND_URL || 'http://localhost:3000';
    res.redirect(`${FRONTEND_URL}/login?error=google_auth_failed`);
  }
};

module.exports = {
  registerUser,
  registerAuthority,
  login,
  getProfile,
  updateProfile,
  changePassword,
  forgotPassword,
  resetPassword,
  logout,
  googleCallback,
  // Exported for reuse in other controllers (e.g., camerasController)
  generateRtspUrlFromCamera,
};
