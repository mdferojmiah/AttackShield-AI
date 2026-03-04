/**
 * API Service Module
 * Centralized Axios-based API client with JWT interceptor
 */

import axios, { AxiosError, InternalAxiosRequestConfig } from 'axios';
import { API_CONFIG, APP_CONFIG } from '@/config';
import { UserStorage } from './storage';
import type {
  ApiResponse,
  LoginResponse,
  LoginFormData,
  UserSignupFormData,
  AuthoritySignupFormData,
  DashboardStats,
  Activity,
  NotificationItem,
  AuthorityAlert,
  AppSettings,
  UserCamera,
  EnsembleMetrics,
  TrustScore,
} from '@/types';

// ─── Axios Instance with JWT Interceptor ───────────────────────────
const api = axios.create({
  baseURL: API_CONFIG.BASE_URL,
  timeout: APP_CONFIG.TIMEOUTS.API_REQUEST,
  headers: { 'Content-Type': 'application/json' },
});

// Request interceptor – attach token
api.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = UserStorage.getToken();
  if (token && config.headers) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Response interceptor – normalise errors
api.interceptors.response.use(
  (response) => response,
  (error: AxiosError<{ error?: string; message?: string }>) => {
    if (error.response?.status === 401) {
      UserStorage.logout();
      window.location.href = '/login';
    }
    return Promise.reject(error);
  },
);

// ─── Generic Helpers ───────────────────────────────────────────────
async function safeRequest<T>(
  fn: () => Promise<{ data: T }>,
): Promise<ApiResponse<T>> {
  try {
    const { data } = await fn();
    return { success: true, data };
  } catch (err) {
    const error = err as AxiosError<{ error?: string; message?: string }>;
    const message =
      error.response?.data?.error ||
      error.response?.data?.message ||
      error.message ||
      'Network error. Please try again.';
    return { success: false, error: message };
  }
}

// ─── Auth API ──────────────────────────────────────────────────────
export const AuthAPI = {
  login(credentials: LoginFormData) {
    return safeRequest<LoginResponse>(() =>
      api.post('/api/auth/login', credentials),
    );
  },

  signupUser(data: UserSignupFormData) {
    return safeRequest<unknown>(() => api.post('/api/auth/signup/user', data));
  },

  signupAuthority(data: AuthoritySignupFormData) {
    return safeRequest<unknown>(() =>
      api.post('/api/auth/signup/authority', data),
    );
  },

  forgotPassword(email: string) {
    return safeRequest<unknown>(() =>
      api.post('/api/auth/forgot-password', { email }),
    );
  },

  resetPassword(token: string, password: string) {
    return safeRequest<unknown>(() =>
      api.post(`/api/auth/reset-password/${token}`, { password }),
    );
  },

  getGoogleAuthUrl(): string {
    return `${API_CONFIG.AUTH_URL}/google`;
  },
};

// ─── Cameras API ───────────────────────────────────────────────────
export const CamerasAPI = {
  async list(): Promise<ApiResponse<UserCamera[]>> {
    const res = await safeRequest<{
      success: boolean;
      data?: UserCamera[];
      error?: string;
    }>(() => api.get('/api/cameras'));

    if (!res.success || !res.data) {
      return { success: false, error: res.error || 'Failed to load cameras' };
    }

    const payload = res.data;
    if (!payload.success || !Array.isArray(payload.data)) {
      return {
        success: false,
        error: payload.error || 'Invalid cameras response',
      };
    }

    return { success: true, data: payload.data };
  },

  add(payload: {
    name: string;
    location: string;
    cameraIp?: string;
    cameraUsername?: string;
    cameraPassword?: string;
    cameraPort?: string;
    cameraBrand?: string;
    cameraPath?: string;
    rtspUrl?: string;
  }) {
    return safeRequest<unknown>(() => api.post('/api/cameras', payload));
  },

  remove(id: string) {
    return safeRequest<unknown>(() => api.delete(`/api/cameras/${id}`));
  },
};

// ─── Stream API ────────────────────────────────────────────────────
export const StreamAPI = {
  startAll() {
    return safeRequest<{
      success: boolean;
      message: string;
      streams: { cameraId: string; hlsUrl: string }[];
    }>(() => api.post('/api/stream/start-all'));
  },

  start(cameraId: string, rtspUrl: string) {
    return safeRequest<{ success: boolean; hlsUrl: string }>(() =>
      api.post('/api/stream/start', { cameraId, rtspUrl }),
    );
  },

  stop(cameraId: string) {
    return safeRequest<unknown>(() =>
      api.post('/api/stream/stop', { cameraId }),
    );
  },

  stopAll() {
    return safeRequest<{ success: boolean; stopped: string[] }>(() =>
      api.post('/api/stream/stop-all'),
    );
  },

  status() {
    return safeRequest<{
      success: boolean;
      streams: { cameraId: string; active: boolean }[];
    }>(() => api.get('/api/stream/status'));
  },

  startWebcam(cameraId?: string, deviceName?: string) {
    return safeRequest<{
      success: boolean;
      cameraId: string;
      hlsUrl: string;
    }>(() => api.post('/api/stream/webcam', { cameraId, deviceName }));
  },
};

// ─── Settings API ──────────────────────────────────────────────────
export const SettingsAPI = {
  get() {
    return safeRequest<AppSettings>(() => api.get('/api/settings'));
  },

  update(settings: Partial<AppSettings>) {
    return safeRequest<AppSettings>(() =>
      api.put('/api/settings', { settings }),
    );
  },
};

// ─── Dashboard API ─────────────────────────────────────────────────
export const DashboardAPI = {
  getStats() {
    return safeRequest<DashboardStats>(() =>
      api.get('/api/dashboard/stats'),
    );
  },

  getActivity() {
    return safeRequest<Activity[]>(() =>
      api.get('/api/dashboard/activity'),
    );
  },

  getEnsembleMetrics() {
    return safeRequest<{ data: EnsembleMetrics }>(() =>
      api.get('/api/dashboard/metrics'),
    );
  },

  getTrustScore() {
    return safeRequest<{ data: TrustScore }>(() =>
      api.get('/api/dashboard/trust-score'),
    );
  },
};

// ─── Notifications API ─────────────────────────────────────────────
export const NotificationsAPI = {
  getAll() {
    return safeRequest<NotificationItem[]>(() =>
      api.get('/api/notifications'),
    );
  },

  markAsRead(id: string) {
    return safeRequest<unknown>(() =>
      api.put(`/api/notifications/${id}/read`),
    );
  },

  markAllAsRead() {
    return safeRequest<unknown>(() =>
      api.put('/api/notifications/read-all'),
    );
  },

  delete(id: string) {
    return safeRequest<unknown>(() =>
      api.delete(`/api/notifications/${id}`),
    );
  },
};

// ─── Alerts API (Authority) ────────────────────────────────────────
export const AlertsAPI = {
  getNew() {
    return safeRequest<AuthorityAlert[]>(() =>
      api.get('/api/alerts/new'),
    );
  },

  getMyActive() {
    return safeRequest<AuthorityAlert[]>(() =>
      api.get('/api/alerts/my-active'),
    );
  },

  accept(id: string) {
    return safeRequest<unknown>(() =>
      api.post(`/api/alerts/${id}/accept`),
    );
  },

  dismiss(id: string) {
    return safeRequest<unknown>(() =>
      api.post(`/api/alerts/${id}/dismiss`),
    );
  },

  resolve(id: string) {
    return safeRequest<unknown>(() =>
      api.post(`/api/alerts/${id}/resolve`),
    );
  },

  getHistory(params?: {
    type?: string;
    startDate?: string;
    endDate?: string;
    q?: string;
  }) {
    return safeRequest<AuthorityAlert[]>(() =>
      api.get('/api/alerts/history', { params }),
    );
  },
};

export default api;
