/**
 * Request Validation Middleware
 * Validates incoming request data
 */

const { ApiError } = require('./errorHandler');

// Validate email format
const isValidEmail = (email) => {
  const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
  return emailRegex.test(email);
};

// Validate password strength
const isValidPassword = (password) => {
  return password && password.length >= 6;
};

// Validate phone number
const isValidPhone = (phone) => {
  const phoneRegex = /^[\d\s\-+()]{10,}$/;
  return phoneRegex.test(phone);
};

// Validate User Signup
const validateUserSignup = (req, res, next) => {
  const {
    name,
    email,
    phone,
    password,
    cctvName,
    rtspUrl,
    location,
    // New camera-friendly fields
    cameraIp,
    cameraUsername,
    cameraPassword,
  } = req.body;
  const errors = [];

  if (!name || name.trim().length < 2) {
    errors.push('Name must be at least 2 characters');
  }

  if (!email || !isValidEmail(email)) {
    errors.push('Please provide a valid email address');
  }

  if (!phone || !isValidPhone(phone)) {
    errors.push('Please provide a valid phone number');
  }

  if (!password || !isValidPassword(password)) {
    errors.push('Password must be at least 6 characters');
  }

  if (!cctvName || cctvName.trim().length === 0) {
    errors.push('CCTV name is required');
  }

  // Either a full RTSP URL OR camera connection details must be provided.
  const hasRtspUrl = rtspUrl && rtspUrl.trim().length > 0;
  const hasCameraDetails = cameraIp && cameraUsername && cameraPassword;

  if (!hasRtspUrl && !hasCameraDetails) {
    errors.push('Please provide camera IP, username and password to configure the stream');
  }

  if (!location || location.trim().length === 0) {
    errors.push('Location is required');
  }

  if (errors.length > 0) {
    return res.status(400).json({
      success: false,
      error: errors.join(', '),
      errors
    });
  }

  next();
};

// Validate Authority Signup
const validateAuthoritySignup = (req, res, next) => {
  const { name, email, officerId, stationName, password } = req.body;
  const errors = [];

  if (!name || name.trim().length < 2) {
    errors.push('Name must be at least 2 characters');
  }

  if (!email || !isValidEmail(email)) {
    errors.push('Please provide a valid email address');
  }

  if (!officerId || officerId.trim().length === 0) {
    errors.push('Officer ID is required');
  }

  if (!stationName || stationName.trim().length === 0) {
    errors.push('Station name is required');
  }

  if (!password || !isValidPassword(password)) {
    errors.push('Password must be at least 6 characters');
  }

  if (errors.length > 0) {
    return res.status(400).json({
      success: false,
      error: errors.join(', '),
      errors
    });
  }

  next();
};

// Validate Login
const validateLogin = (req, res, next) => {
  const { email, password } = req.body;
  const errors = [];

  if (!email || !isValidEmail(email)) {
    errors.push('Please provide a valid email address');
  }

  if (!password || password.length === 0) {
    errors.push('Password is required');
  }

  if (errors.length > 0) {
    return res.status(400).json({
      success: false,
      error: errors.join(', '),
      errors
    });
  }

  next();
};

// Validate Password Reset Request
const validateForgotPassword = (req, res, next) => {
  const { email } = req.body;

  if (!email || !isValidEmail(email)) {
    return res.status(400).json({
      success: false,
      error: 'Please provide a valid email address'
    });
  }

  next();
};

module.exports = {
  validateUserSignup,
  validateAuthoritySignup,
  validateLogin,
  validateForgotPassword
};
