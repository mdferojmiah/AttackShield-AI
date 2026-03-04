/**
 * Authentication Routes
 * Handles user and authority authentication
 */

const express = require('express');
const router = express.Router();
const passport = require('passport');

const {
  registerUser,
  registerAuthority,
  login,
  getProfile,
  updateProfile,
  changePassword,
  forgotPassword,
  resetPassword,
  logout,
  googleCallback
} = require('../controllers/authController');

const { authenticate, authorize } = require('../middleware/auth');
const { 
  validateUserSignup, 
  validateAuthoritySignup, 
  validateLogin,
  validateForgotPassword 
} = require('../middleware/validation');

// Public routes
router.post('/signup/user', validateUserSignup, registerUser);
router.post('/signup/authority', validateAuthoritySignup, registerAuthority);
router.post('/login', validateLogin, login);
router.post('/forgot-password', validateForgotPassword, forgotPassword);
router.post('/reset-password/:token', resetPassword);

// Protected routes
router.get('/me', authenticate, getProfile);
router.put('/profile', authenticate, updateProfile);
router.put('/change-password', authenticate, changePassword);
router.post('/logout', authenticate, logout);

// Google OAuth routes
router.get('/google', passport.authenticate('google', { scope: ['profile', 'email'] }));
router.get('/google/callback',
  passport.authenticate('google', { session: false, failureRedirect: (process.env.FRONTEND_URL || 'http://localhost:3000') + '/login?error=google_auth_failed' }),
  googleCallback
);

module.exports = router;
