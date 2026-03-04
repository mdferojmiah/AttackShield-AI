/**
 * Theme Context
 * Manages dark/light theme via Tailwind 'dark' class strategy
 */

import React, {
  createContext,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from 'react';
import type { ThemeMode, AppSettings } from '@/types';
import { SettingsStorage } from '@/services/storage';
import { SettingsAPI } from '@/services/api';

interface ThemeContextType {
  mode: ThemeMode;
  setMode: (mode: ThemeMode) => void;
  isDark: boolean;
  toggle: () => void;
}

const ThemeContext = createContext<ThemeContextType | undefined>(undefined);

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [mode, setModeState] = useState<ThemeMode>('dark');

  // On mount, read from local settings
  useEffect(() => {
    const settings = SettingsStorage.getSettings<AppSettings>();
    if (settings?.app?.theme) {
      setModeState(settings.app.theme);
    }
  }, []);

  // Sync <html> class
  useEffect(() => {
    const root = document.documentElement;
    if (mode === 'dark') {
      root.classList.add('dark');
    } else {
      root.classList.remove('dark');
    }
  }, [mode]);

  const setMode = (next: ThemeMode) => {
    setModeState(next);
    SettingsStorage.updateSettings<AppSettings>({ app: { theme: next } } as Partial<AppSettings>);
    SettingsAPI.update({ app: { theme: next } }).catch(() => {
      /* best-effort server sync */
    });
  };

  const toggle = () => setMode(mode === 'dark' ? 'light' : 'dark');

  const value: ThemeContextType = {
    mode,
    setMode,
    isDark: mode === 'dark',
    toggle,
  };

  return (
    <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>
  );
}

export function useTheme() {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme must be used within ThemeProvider');
  return ctx;
}
