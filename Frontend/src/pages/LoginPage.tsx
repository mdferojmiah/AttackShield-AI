/**
 * Login Page
 */

import React, { useState, useEffect } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { HiUser, HiLockClosed, HiEye, HiEyeSlash, HiShieldCheck, HiCheckCircle, HiEnvelope } from 'react-icons/hi2';
import { AuthAPI } from '@/services/api';
import { useAuth } from '@/context';
import { UserStorage } from '@/services/storage';
import { isValidEmail } from '@/utils/helpers';
import { useDocumentTitle } from '@/hooks';
import toast from 'react-hot-toast';

export default function LoginPage() {
  useDocumentTitle('Login');
  const navigate = useNavigate();
  const { login, isAuthenticated, isLoading: authLoading, error: authError, clearError, user } = useAuth();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [passwordVisible, setPasswordVisible] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  // Forgot password modal state
  const [forgotOpen, setForgotOpen] = useState(false);
  const [forgotEmail, setForgotEmail] = useState('');
  const [forgotLoading, setForgotLoading] = useState(false);
  const [forgotSuccess, setForgotSuccess] = useState(false);

  // Navigate on successful auth
  useEffect(() => {
    if (isAuthenticated && user) {
      navigate(user.role === 'authority' ? '/authority/dashboard' : '/dashboard');
    }
  }, [isAuthenticated, user, navigate]);

  // Handle Google OAuth callback
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const token = params.get('token');
    const userData = params.get('user');
    const oauthError = params.get('error');

    if (oauthError) {
      setError('Google sign-in failed. Please try again.');
      // Clean URL
      window.history.replaceState({}, '', '/login');
      return;
    }

    if (token && userData) {
      try {
        const parsed = JSON.parse(userData);
        UserStorage.setUser(parsed);
        UserStorage.setToken(token);
        // Redirect — full reload ensures AuthContext picks up stored data
        window.location.href = parsed.role === 'authority' ? '/authority/dashboard' : '/dashboard';
      } catch {
        setError('Failed to process Google sign-in data.');
        window.history.replaceState({}, '', '/login');
      }
    }
  }, []);

  useEffect(() => {
    if (authError) {
      setError(authError);
      clearError();
    }
  }, [authError, clearError]);

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !password) {
      setError('Please enter email and password');
      return;
    }
    if (!isValidEmail(email)) {
      setError('Please enter a valid email address');
      return;
    }
    setError('');
    setLoading(true);
    try {
      const success = await login({ email, password });
      if (!success) setError('Invalid credentials. Please try again.');
    } catch {
      setError('Network error. Please try again.');
    } finally {
      setLoading(false);
    }
  };

  const handleForgotPassword = async () => {
    if (!forgotEmail || !isValidEmail(forgotEmail)) {
      toast.error('Please enter a valid email');
      return;
    }
    setForgotLoading(true);
    try {
      const result = await AuthAPI.forgotPassword(forgotEmail);
      if (result.success) {
        setForgotSuccess(true);
      } else {
        toast.error(result.error || 'Failed to send reset email');
      }
    } catch {
      toast.error('Network error');
    } finally {
      setForgotLoading(false);
    }
  };

  const closeForgotModal = () => {
    setForgotOpen(false);
    setForgotEmail('');
    setForgotSuccess(false);
  };

  const handleGoogleSignIn = () => {
    window.location.href = AuthAPI.getGoogleAuthUrl();
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-light-bg dark:bg-dark-bg px-4 transition-colors duration-200">
      <div className="w-full max-w-md bg-light-card dark:bg-dark-card rounded-3xl p-8 shadow-2xl border border-light-border dark:border-dark-border">
        {/* Brand */}
        <div className="flex items-center justify-center gap-2 mb-6">
          <HiShieldCheck className="text-primary" size={28} />
          <h1 className="text-2xl font-bold text-slate-800 dark:text-white">AttackShield AI</h1>
        </div>

        {error && (
          <div className="bg-red-500/10 border border-red-500/30 text-red-400 text-sm rounded-xl px-4 py-3 mb-4 text-center">
            {error}
          </div>
        )}

        <form onSubmit={handleLogin} className="space-y-4">
          {/* Email */}
          <div className="relative">
            <HiUser className="absolute left-4 top-1/2 -translate-y-1/2 text-slate-400" size={18} />
            <input
              type="email"
              placeholder="Email"
              className="input-field pl-11"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              autoComplete="email"
            />
          </div>

          {/* Password */}
          <div className="relative">
            <HiLockClosed className="absolute left-4 top-1/2 -translate-y-1/2 text-slate-400" size={18} />
            <input
              type={passwordVisible ? 'text' : 'password'}
              placeholder="Password"
              className="input-field pl-11 pr-11"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
            />
            <button
              type="button"
              onClick={() => setPasswordVisible(!passwordVisible)}
              className="absolute right-4 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-200"
            >
              {passwordVisible ? <HiEyeSlash size={18} /> : <HiEye size={18} />}
            </button>
          </div>

          {/* Submit */}
          <button
            type="submit"
            disabled={loading || authLoading}
            className="btn-accent w-full py-3.5 text-lg"
          >
            {loading || authLoading ? (
              <span className="inline-block w-5 h-5 border-2 border-white border-t-transparent rounded-full animate-spin" />
            ) : (
              'Login'
            )}
          </button>
        </form>

        {/* Forgot Password */}
        <button
          onClick={() => setForgotOpen(true)}
          className="block mx-auto mt-4 text-sm text-accent hover:underline"
        >
          Forgot Password?
        </button>

        {/* Divider */}
        <div className="flex items-center gap-3 my-6">
          <div className="flex-1 h-px bg-light-border dark:bg-dark-border" />
          <span className="text-xs text-slate-500">Or</span>
          <div className="flex-1 h-px bg-light-border dark:bg-dark-border" />
        </div>

        {/* Google */}
        <button
          onClick={handleGoogleSignIn}
          disabled={loading}
          className="w-full flex items-center justify-center gap-3 py-3 rounded-xl border border-light-border dark:border-dark-border bg-light-surface dark:bg-dark-elevated text-slate-800 dark:text-white hover:bg-slate-100 dark:hover:bg-dark-card transition-colors"
        >
          <svg className="w-5 h-5" viewBox="0 0 24 24">
            <path
              fill="#FF6D00"
              d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 01-2.2 3.32v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.1z"
            />
            <path
              fill="#FF6D00"
              d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z"
            />
            <path
              fill="#FF6D00"
              d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z"
            />
            <path
              fill="#FF6D00"
              d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z"
            />
          </svg>
          Continue with Google
        </button>

        {/* Links */}
        <p className="text-center mt-5 text-sm text-slate-400">
          Don't have an account?{' '}
          <Link to="/signup" className="text-accent hover:underline">
            Sign Up
          </Link>
        </p>
        <p className="text-center mt-2 text-sm text-slate-400">
          Authority Signup?{' '}
          <Link to="/authority/signup" className="text-accent hover:underline">
            Click Here
          </Link>
        </p>
      </div>

      {/* Forgot Password Modal */}
      {forgotOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 px-4">
          <div className="bg-white rounded-3xl max-w-md w-full p-8 relative">
            <button onClick={closeForgotModal} className="absolute top-4 right-4 text-slate-400 hover:text-slate-600">
              ✕
            </button>

            {forgotSuccess ? (
              <div className="text-center space-y-4">
                <HiCheckCircle className="mx-auto text-green-500" size={64} />
                <h3 className="text-xl font-bold text-slate-800">Email Sent!</h3>
                <p className="text-sm text-slate-500">Check your email for reset instructions.</p>
                <button onClick={closeForgotModal} className="btn-accent w-full">
                  Done
                </button>
              </div>
            ) : (
              <div className="space-y-4">
                <HiLockClosed className="mx-auto text-accent" size={48} />
                <h3 className="text-xl font-bold text-slate-800 text-center">Forgot Password</h3>
                <p className="text-sm text-slate-500 text-center">
                  Enter your email and we'll send you a reset link.
                </p>
                <input
                  type="email"
                  placeholder="Your email"
                  className="w-full rounded-xl border border-slate-200 px-4 py-3 text-slate-800 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-accent/50"
                  value={forgotEmail}
                  onChange={(e) => setForgotEmail(e.target.value)}
                />
                <button
                  onClick={handleForgotPassword}
                  disabled={forgotLoading}
                  className="btn-accent w-full"
                >
                  {forgotLoading ? 'Sending...' : 'Send Reset Link'}
                </button>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
