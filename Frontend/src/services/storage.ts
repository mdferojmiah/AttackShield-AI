/**
 * Storage Service Module
 * localStorage operations with type safety (replaces AsyncStorage)
 */

import { APP_CONFIG } from '@/config';
import type { User } from '@/types';

// Generic storage operations (synchronous – no async needed for localStorage)
export const Storage = {
  get<T>(key: string): T | null {
    try {
      const value = localStorage.getItem(key);
      return value ? (JSON.parse(value) as T) : null;
    } catch (error) {
      console.error(`Error reading ${key} from storage:`, error);
      return null;
    }
  },

  set<T>(key: string, value: T): boolean {
    try {
      localStorage.setItem(key, JSON.stringify(value));
      return true;
    } catch (error) {
      console.error(`Error writing ${key} to storage:`, error);
      return false;
    }
  },

  remove(key: string): boolean {
    try {
      localStorage.removeItem(key);
      return true;
    } catch (error) {
      console.error(`Error removing ${key} from storage:`, error);
      return false;
    }
  },

  clear(): boolean {
    try {
      localStorage.clear();
      return true;
    } catch (error) {
      console.error('Error clearing storage:', error);
      return false;
    }
  },
};

// User-specific storage operations
export const UserStorage = {
  getUser(): User | null {
    return Storage.get<User>(APP_CONFIG.STORAGE_KEYS.USER_DATA);
  },

  setUser(user: User): boolean {
    return Storage.set(APP_CONFIG.STORAGE_KEYS.USER_DATA, user);
  },

  removeUser(): boolean {
    return Storage.remove(APP_CONFIG.STORAGE_KEYS.USER_DATA);
  },

  getToken(): string | null {
    return Storage.get<string>(APP_CONFIG.STORAGE_KEYS.AUTH_TOKEN);
  },

  setToken(token: string): boolean {
    return Storage.set(APP_CONFIG.STORAGE_KEYS.AUTH_TOKEN, token);
  },

  removeToken(): boolean {
    return Storage.remove(APP_CONFIG.STORAGE_KEYS.AUTH_TOKEN);
  },

  logout(): boolean {
    this.removeUser();
    this.removeToken();
    return true;
  },
};

// Settings storage operations
export const SettingsStorage = {
  getSettings<T>(): T | null {
    return Storage.get<T>(APP_CONFIG.STORAGE_KEYS.SETTINGS);
  },

  setSettings<T>(settings: T): boolean {
    return Storage.set(APP_CONFIG.STORAGE_KEYS.SETTINGS, settings);
  },

  updateSettings<T extends object>(updates: Partial<T>): boolean {
    const current = this.getSettings<T>();
    const updated = { ...current, ...updates } as T;
    return this.setSettings(updated);
  },

  clearAll(): boolean {
    return Storage.remove(APP_CONFIG.STORAGE_KEYS.SETTINGS);
  },
};
