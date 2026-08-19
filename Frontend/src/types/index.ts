/**
 * Application Type Definitions
 * Centralized TypeScript interfaces and types
 */

// User Types
export interface User {
  id?: string;
  _id?: string;
  name: string;
  email: string;
  phone?: string;
  role: 'user' | 'authority' | 'senior_authority' | 'admin';
  cctvName?: string;
  rtspUrl?: string;
  location?: string;
  camera?: Camera;
  cameras?: UserCamera[];
}

export interface Authority {
  _id: string;
  name: string;
  email: string;
  officerId: string;
  stationName: string;
  role: 'authority';
}

// Camera Types
export interface Camera {
  camera_name: string;
  stream_url: string;
  location: string;
  rtsp_url?: string;
}

export interface UserCamera {
  id: string;
  name: string;
  rtspUrl: string;
  location: string;
  brand?: string;
}

export interface AdminOverview {
  totalUsers: number;
  activeUsers: number;
  totalCameras: number;
  users: Array<{
    id: string;
    name: string;
    email: string;
    phone?: string;
    role: string;
    isActive: boolean;
    lastLogin?: string;
    createdAt: string;
    cameras: Array<{
      id: string;
      name: string;
      location?: string;
      brand?: string;
      isPrimary: boolean;
    }>;
  }>;
  recentActions: Array<{
    id: string;
    userId?: string;
    userName: string;
    type: string;
    label: string;
    location: string;
    cameraName?: string;
    confidence: number;
    createdAt: string;
  }>;
}

export interface HitListEntry {
  id: string;
  name: string;
  imageUrl: string;
  notes?: string;
  createdAt: string;
}

// Settings Types
export type ThemeMode = 'dark' | 'light';

export interface NotificationSettings {
  push: boolean;
  sound: boolean;
  vibration: boolean;
  email: boolean;
}

export interface DetectionSettings {
  sensitivity: 'low' | 'medium' | 'high';
  alertThreshold: number;
}

export interface AppSettings {
  notifications: NotificationSettings;
  detection: DetectionSettings;
  app: { theme: ThemeMode };
  // Flat convenience keys used by SettingsPage
  notificationsEnabled?: boolean;
  soundEnabled?: boolean;
  vibrationEnabled?: boolean;
  emailNotifications?: boolean;
  detectionSensitivity?: 'low' | 'medium' | 'high' | 'max';
  alertThreshold?: number;
  darkMode?: boolean;
  autoStartMonitoring?: boolean;
}

/** Email alert throttle. `nextAllowedAt` is an ISO-8601 UTC instant, null when idle. */
export interface EmailCooldown {
  enabled: boolean;
  cooldownMinutes: number;
  alertType?: string | null;
  nextAllowedAt?: string | null;
  /** Present only on realtime pushes: false means that alert's email was throttled. */
  sent?: boolean;
}

// Dashboard Types
export interface DashboardStats {
  totalWeapons: number;
  alertsSent: number;
  accuracy: number;
  suspiciousActivities: number;
  facesDetected: number;
  uniquePersons: number;
  trustScore: number;
  ensembleConfidence: number;
}

export interface EnsembleMetrics {
  weapons_detected: number;
  suspicious_activities: number;
  faces_detected: number;
  unique_persons: number;
  total_frames_processed: number;
  avg_inference_latency_ms: number;
  ensemble_confidence: number;
}

export interface TrustScore {
  score: number;
  auth_consistency: number;
  anomaly_frequency: number;
  model_confidence_stability: number;
  communication_integrity: number;
  policy_compliance: number;
}

export interface Activity {
  id: string;
  type: 'high' | 'medium' | 'low';
  message: string;
  time: string;
}

// Notification Types
export type NotificationType =
  | 'suspicious'
  | 'vehicle'
  | 'loitering'
  | 'package'
  | 'camera'
  | 'weapon'
  | 'hit_list'
  | 'system';

export interface NotificationItem {
  _id: string;
  id?: string;
  type: NotificationType;
  title: string;
  time?: string;
  createdAt: string;
  description: string;
  message?: string;
  icon?: string;
  isRead?: boolean;
  read?: boolean;
  location?: string;
  confidence?: number;
  alertId?: string;
  cameraName?: string;
  activity?: string;
  person?: string;
  imageUrl?: string;
  mapUrl?: string;
}

// Alert Types for Authority dashboard
export type AlertPriority = 'high' | 'medium' | 'low';
export type AlertStatus = 'new' | 'pending' | 'accepted' | 'dismissed' | 'resolved';

export interface AuthorityAlert {
  _id: string;
  id?: string;
  type: AlertPriority;
  title?: string;
  message: string;
  location?: string;
  imageUrl?: string;
  status: AlertStatus;
  createdAt: string;
  weaponType?: string;
  incidentType?: string;
  priority?: AlertPriority;
  confidence?: number;
  cameraName?: string;
  reportedBy?: string;
  description?: string;
}

export interface NotificationTypeConfig {
  iconName: string;
  iconColor: string;
  titleColor: string;
}

// API Response Types
export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  error?: string;
  message?: string;
}

export interface LoginResponse {
  token: string;
  user: User;
  role: string;
}

export interface StreamStatus {
  isRunning: boolean;
  hlsReady: boolean;
  error?: string;
}

// Form Types
export interface LoginFormData {
  email: string;
  password: string;
}

export interface UserSignupFormData {
  name: string;
  email: string;
  phone: string;
  password: string;
}

export interface AuthoritySignupFormData {
  name: string;
  email: string;
  officerId: string;
  stationName: string;
  password: string;
}
