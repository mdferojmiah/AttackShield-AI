/**
 * Passport.js Configuration
 * Google OAuth 2.0 Strategy
 */

const passport = require('passport');
const GoogleStrategy = require('passport-google-oauth20').Strategy;
const User = require('../models/User');

const BACKEND_URL = process.env.BACKEND_URL || 'http://localhost:5000';

if (!process.env.GOOGLE_CLIENT_ID || !process.env.GOOGLE_CLIENT_SECRET) {
  console.warn('[Auth] Google OAuth credentials not set. Google login will be unavailable.');
} else {
  passport.use(
    new GoogleStrategy(
      {
        clientID: process.env.GOOGLE_CLIENT_ID,
        clientSecret: process.env.GOOGLE_CLIENT_SECRET,
        callbackURL: `${BACKEND_URL}/api/auth/google/callback`,
      },
      async (accessToken, refreshToken, profile, done) => {
      try {
        // Check if user already exists with this Google ID
        let user = await User.findOne({ googleId: profile.id });

        if (user) {
          return done(null, user);
        }

        // Check if user exists with the same email
        const email =
          profile.emails && profile.emails.length > 0
            ? profile.emails[0].value
            : null;

        if (email) {
          user = await User.findOne({ email: email.toLowerCase() });
          if (user) {
            // Link Google account to existing user
            user.googleId = profile.id;
            user.avatar =
              profile.photos && profile.photos.length > 0
                ? profile.photos[0].value
                : user.avatar;
            await user.save({ validateBeforeSave: false });
            return done(null, user);
          }
        }

        // Create a new user from Google profile
        user = await User.create({
          googleId: profile.id,
          name: profile.displayName || `${profile.name?.givenName || ''} ${profile.name?.familyName || ''}`.trim(),
          email: email,
          avatar:
            profile.photos && profile.photos.length > 0
              ? profile.photos[0].value
              : undefined,
          role: 'user',
          isActive: true,
        });

        return done(null, user);
      } catch (error) {
        console.error('Google OAuth error:', error);
        return done(error, null);
      }
    }
  )
);
}

// Serialization (not needed for JWT-based auth, but required by passport)
passport.serializeUser((user, done) => {
  done(null, user._id);
});

passport.deserializeUser(async (id, done) => {
  try {
    const user = await User.findById(id);
    done(null, user);
  } catch (error) {
    done(error, null);
  }
});

module.exports = passport;
