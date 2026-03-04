/**
 * Helper Functions Module
 * Common utility functions used across the app
 */

import type { NotificationTypeConfig } from '@/types';

export const isValidEmail = (email: string): boolean => {
  const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
  return emailRegex.test(email);
};

export const isValidPassword = (password: string): boolean => {
  return password.length >= 6;
};

export const isValidPhone = (phone: string): boolean => {
  const phoneRegex = /^[\d\s\-+()]{10,}$/;
  return phoneRegex.test(phone);
};

export const formatTime = (date: Date | string): string => {
  const d = typeof date === 'string' ? new Date(date) : date;
  return d.toLocaleTimeString('en-US', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: true,
  });
};

export const formatDate = (date: Date | string): string => {
  const d = typeof date === 'string' ? new Date(date) : date;
  return d.toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
};

export const formatTimeAgo = (date: Date | string): string =>
  formatRelativeTime(date);

export const formatRelativeTime = (date: Date | string): string => {
  const d = typeof date === 'string' ? new Date(date) : date;
  const now = new Date();
  const diff = now.getTime() - d.getTime();

  const seconds = Math.floor(diff / 1000);
  const minutes = Math.floor(seconds / 60);
  const hours = Math.floor(minutes / 60);
  const days = Math.floor(hours / 24);

  if (days > 0) return `${days} day${days > 1 ? 's' : ''} ago`;
  if (hours > 0) return `${hours} hour${hours > 1 ? 's' : ''} ago`;
  if (minutes > 0) return `${minutes} minute${minutes > 1 ? 's' : ''} ago`;
  return 'Just now';
};

export const formatPercentage = (value: number): string => {
  return `${(value * 100).toFixed(1)}%`;
};

export const truncateText = (text: string, maxLength: number): string => {
  if (text.length <= maxLength) return text;
  return text.substring(0, maxLength - 3) + '...';
};

export const getNotificationTypeConfig = (
  type: string,
): NotificationTypeConfig => {
  const configs: Record<string, NotificationTypeConfig> = {
    suspicious: {
      iconName: 'HiExclamationTriangle',
      iconColor: '#FF4C4C',
      titleColor: '#FF6A6A',
    },
    weapon: {
      iconName: 'HiExclamationCircle',
      iconColor: '#FF4C4C',
      titleColor: '#FF6A6A',
    },
    vehicle: {
      iconName: 'HiTruck',
      iconColor: '#4AA9FF',
      titleColor: '#5FB3FF',
    },
    loitering: {
      iconName: 'HiUser',
      iconColor: '#4ED47A',
      titleColor: '#78EBA0',
    },
    package: {
      iconName: 'HiCube',
      iconColor: '#FFDA5B',
      titleColor: '#FFD875',
    },
    camera: {
      iconName: 'HiVideoCamera',
      iconColor: '#B983FF',
      titleColor: '#C99EFF',
    },
    system: {
      iconName: 'HiCog6Tooth',
      iconColor: '#4AA9FF',
      titleColor: '#5FB3FF',
    },
  };

  return configs[type] || configs.system;
};

export const debounce = <T extends (...args: unknown[]) => unknown>(
  func: T,
  wait: number,
): ((...args: Parameters<T>) => void) => {
  let timeout: ReturnType<typeof setTimeout> | null = null;

  return (...args: Parameters<T>) => {
    if (timeout) clearTimeout(timeout);
    timeout = setTimeout(() => func(...args), wait);
  };
};

export const throttle = <T extends (...args: unknown[]) => unknown>(
  func: T,
  limit: number,
): ((...args: Parameters<T>) => void) => {
  let inThrottle = false;

  return (...args: Parameters<T>) => {
    if (!inThrottle) {
      func(...args);
      inThrottle = true;
      setTimeout(() => (inThrottle = false), limit);
    }
  };
};

/** Return a CSS class string for alert priority badges */
export const getPriorityClasses = (
  type?: string,
): string => {
  switch (type) {
    case 'high':
      return 'bg-red-500/10 text-red-400 border-red-500/30';
    case 'medium':
      return 'bg-amber-500/10 text-amber-400 border-amber-500/30';
    case 'low':
    default:
      return 'bg-emerald-500/10 text-emerald-400 border-emerald-500/30';
  }
};
