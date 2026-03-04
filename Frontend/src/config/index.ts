/**
 * Application Configuration
 * Centralized configuration for API endpoints and app settings
 */

const BASE_URL = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';

export const API_CONFIG = {
  BASE_URL,
  AUTH_URL: `${BASE_URL}/api/auth`,
  DASHBOARD_URL: `${BASE_URL}/api/dashboard`,
  NOTIFICATIONS_URL: `${BASE_URL}/api/notifications`,
  ALERTS_URL: `${BASE_URL}/api/alerts`,
  CAMERAS_URL: `${BASE_URL}/api/cameras`,
  SETTINGS_URL: `${BASE_URL}/api/settings`,
  STREAM_URL: `${BASE_URL}/api/stream`,
};

export const APP_CONFIG = {
  APP_NAME: 'AttackShield AI',
  VERSION: '1.0.0',
  STORAGE_KEYS: {
    USER_DATA: 'wds_userData',
    AUTH_TOKEN: 'wds_authToken',
    SETTINGS: 'wds_appSettings',
  },
  TIMEOUTS: {
    API_REQUEST: 30000,
    STREAM_CHECK: 3000,
  },
};

export const THEME_COLORS = {
  primary: '#3B82F6',
  secondary: '#6366F1',
  success: '#10B981',
  warning: '#F59E0B',
  danger: '#EF4444',
  accent: '#FF6D00',
  background: {
    dark: '#0F172A',
    light: '#F8FAFC',
  },
  card: {
    dark: '#1E293B',
    light: '#FFFFFF',
  },
  text: {
    primary: '#F8FAFC',
    secondary: '#94A3B8',
    dark: '#0F172A',
  },
};
