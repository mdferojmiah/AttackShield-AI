import React, { useState, useEffect, useCallback } from 'react';
import {
  HiMoon,
  HiSun,
  HiBell,
  HiBellSlash,
  HiShieldCheck,
  HiUser,
  HiArrowRightOnRectangle,
  HiTrash,
  HiChevronLeft,
  HiChevronRight,
  HiCog8Tooth,
  HiEnvelope,
  HiMapPin,
  HiVideoCamera,
  HiExclamationTriangle,
} from 'react-icons/hi2';
import { SettingsAPI } from '@/services/api';
import { UserStorage, SettingsStorage } from '@/services/storage';
import { useAuth, useTheme } from '@/context';
import { useDocumentTitle } from '@/hooks';
import { LoadingSpinner } from '@/components';
import type { AppSettings } from '@/types';
import toast from 'react-hot-toast';

const SENSITIVITY_OPTIONS = ['low', 'medium', 'high', 'max'] as const;

export default function SettingsPage() {
  useDocumentTitle('Settings');

  const { user, logout } = useAuth();
  const { isDark, toggle: toggleTheme } = useTheme();

  const [settings, setSettings] = useState<AppSettings>({
    notifications: { push: true, sound: true, vibration: true },
    detection: { sensitivity: 'medium', alertThreshold: 5 },
    app: { theme: isDark ? 'dark' : 'light' },
    notificationsEnabled: true,
    detectionSensitivity: 'medium',
    alertThreshold: 5,
    darkMode: isDark,
    soundEnabled: true,
    vibrationEnabled: true,
    autoStartMonitoring: false,
  });
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);

  const loadSettings = useCallback(async () => {
    setLoading(true);
    try {
      const res = await SettingsAPI.get();
      if (res.success && res.data) {
        const d = res.data as Record<string, unknown>;
        setSettings((prev) => ({ ...prev, ...d }));
        SettingsStorage.setSettings({ ...settings, ...d });
      } else {
        const local = SettingsStorage.getSettings<AppSettings>();
        if (local) setSettings((prev) => ({ ...prev, ...local }));
      }
    } catch {
      const local = SettingsStorage.getSettings<AppSettings>();
      if (local) setSettings((prev) => ({ ...prev, ...local }));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadSettings();
  }, [loadSettings]);

  // Persist a setting change
  const updateSetting = async <K extends keyof AppSettings>(
    key: K,
    value: AppSettings[K],
  ) => {
    const next = { ...settings, [key]: value };
    setSettings(next);
    SettingsStorage.setSettings(next);

    if (key === 'darkMode') {
      toggleTheme();
    }

    try {
      await SettingsAPI.update({ [key]: value });
    } catch {
      // saved locally, silent
    }
  };

  const handleClearData = () => {
    if (!window.confirm('Clear all locally cached data?')) return;
    SettingsStorage.clearAll();
    toast.success('Local data cleared');
  };

  const handleLogout = () => {
    if (!window.confirm('Are you sure you want to log out?')) return;
    logout();
    toast.success('Logged out');
  };

  if (loading) return <LoadingSpinner text="Loading Settings..." />;

  return (
    <div className="p-4 lg:p-8 max-w-2xl mx-auto space-y-8">
      <h2 className="text-xl font-bold text-slate-800 dark:text-white">Settings</h2>

      {/* Profile Section */}
      <section className="card space-y-4">
        <h3 className="text-sm font-bold text-slate-500 dark:text-slate-300 uppercase tracking-wider mb-2">
          Profile
        </h3>
        <div className="flex items-center gap-4">
          <div className="w-14 h-14 rounded-full bg-accent flex items-center justify-center text-white text-xl font-bold uppercase">
            {user?.name?.[0] || 'U'}
          </div>
          <div className="min-w-0">
            <p className="font-semibold text-slate-800 dark:text-white truncate">
              {user?.name || 'User'}
            </p>
            <p className="text-xs text-slate-500 dark:text-slate-400 truncate flex items-center gap-1">
              <HiEnvelope size={12} /> {user?.email}
            </p>
          </div>
        </div>
        {user?.location && (
          <p className="text-xs text-slate-500 dark:text-slate-400 flex items-center gap-1">
            <HiMapPin size={14} /> {user.location}
          </p>
        )}
        {user?.cctvName && (
          <p className="text-xs text-slate-500 dark:text-slate-400 flex items-center gap-1">
            <HiVideoCamera size={14} /> {user.cctvName}
          </p>
        )}
      </section>

      {/* Notifications */}
      <section className="card space-y-4">
        <h3 className="text-sm font-bold text-slate-500 dark:text-slate-300 uppercase tracking-wider mb-2">
          Notifications
        </h3>
        <ToggleRow
          icon={<HiBell size={20} />}
          label="Push Notifications"
          description="Receive alerts for threat detections"
          value={settings.notificationsEnabled ?? true}
          onChange={(v) => updateSetting('notificationsEnabled', v)}
        />
        <ToggleRow
          icon={<HiBell size={20} />}
          label="Sound"
          description="Play a sound for new alerts"
          value={settings.soundEnabled ?? true}
          onChange={(v) => updateSetting('soundEnabled', v)}
        />
      </section>

      {/* Detection */}
      <section className="card space-y-5">
        <h3 className="text-sm font-bold text-slate-500 dark:text-slate-300 uppercase tracking-wider mb-2">
          Detection
        </h3>

        {/* Sensitivity */}
        <div>
          <label className="text-sm text-slate-600 dark:text-slate-300 font-medium block mb-2">
            Detection Sensitivity
          </label>
          <div className="flex gap-2">
            {SENSITIVITY_OPTIONS.map((opt) => (
              <button
                key={opt}
                onClick={() => updateSetting('detectionSensitivity', opt)}
                className={`flex-1 py-2 rounded-lg text-xs font-bold uppercase transition-colors ${
                  settings.detectionSensitivity === opt
                    ? 'bg-accent text-white'
                    : 'bg-light-surface dark:bg-dark-surface text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-white'
                }`}
              >
                {opt}
              </button>
            ))}
          </div>
        </div>

        {/* Alert Threshold */}
        <div>
          <label className="text-sm text-slate-600 dark:text-slate-300 font-medium block mb-2">
            Alert Threshold
          </label>
          <div className="flex items-center gap-4">
            <button
              onClick={() =>
                updateSetting(
                  'alertThreshold',
                  Math.max(1, (settings.alertThreshold ?? 5) - 1),
                )
              }
              className="p-2 rounded-lg bg-light-surface dark:bg-dark-surface text-slate-600 dark:text-slate-300 hover:bg-slate-200 dark:hover:bg-dark-bg"
            >
              <HiChevronLeft size={20} />
            </button>
            <span className="text-2xl font-bold text-accent w-10 text-center">
              {settings.alertThreshold ?? 5}
            </span>
            <button
              onClick={() =>
                updateSetting(
                  'alertThreshold',
                  Math.min(20, (settings.alertThreshold ?? 5) + 1),
                )
              }
              className="p-2 rounded-lg bg-light-surface dark:bg-dark-surface text-slate-600 dark:text-slate-300 hover:bg-slate-200 dark:hover:bg-dark-bg"
            >
              <HiChevronRight size={20} />
            </button>
          </div>
        </div>

        {/* Auto Start */}
        <ToggleRow
          icon={<HiShieldCheck size={20} />}
          label="Auto-start Monitoring"
          description="Begin detection when the app loads"
          value={settings.autoStartMonitoring ?? false}
          onChange={(v) => updateSetting('autoStartMonitoring', v)}
        />
      </section>

      {/* Appearance */}
      <section className="card space-y-4">
        <h3 className="text-sm font-bold text-slate-500 dark:text-slate-300 uppercase tracking-wider mb-2">
          Appearance
        </h3>
        <ToggleRow
          icon={isDark ? <HiMoon size={20} /> : <HiSun size={20} />}
          label="Dark Mode"
          description="Toggle dark / light theme"
          value={isDark}
          onChange={() => updateSetting('darkMode', !isDark)}
        />
      </section>

      {/* Danger Zone */}
      <section className="space-y-3">
        <button
          onClick={handleClearData}
          className="w-full flex items-center gap-3 card hover:bg-red-500/10 transition-colors"
        >
          <HiTrash className="text-red-400" size={20} />
          <span className="text-red-400 font-medium text-sm">
            Clear Cached Data
          </span>
        </button>

        <button
          onClick={handleLogout}
          className="w-full flex items-center gap-3 card hover:bg-red-500/10 transition-colors"
        >
          <HiArrowRightOnRectangle className="text-red-400" size={20} />
          <span className="text-red-400 font-medium text-sm">Log Out</span>
        </button>
      </section>
    </div>
  );
}

// ─── Reusable Toggle Row ─────────────────────────────────────────
function ToggleRow({
  icon,
  label,
  description,
  value,
  onChange,
}: {
  icon: React.ReactNode;
  label: string;
  description: string;
  value: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <div className="flex items-center justify-between gap-4">
      <div className="flex items-center gap-3 min-w-0">
        <span className="text-slate-500 dark:text-slate-400 flex-shrink-0">{icon}</span>
        <div className="min-w-0">
          <p className="text-sm font-medium text-slate-800 dark:text-white">{label}</p>
          <p className="text-xs text-slate-500 dark:text-slate-400">{description}</p>
        </div>
      </div>
      <button
        onClick={() => onChange(!value)}
        className={`relative w-11 h-6 rounded-full transition-colors flex-shrink-0 ${
          value ? 'bg-accent' : 'bg-slate-600'
        }`}
      >
        <span
          className={`absolute top-0.5 left-0.5 w-5 h-5 rounded-full bg-white transition-transform ${
            value ? 'translate-x-5' : 'translate-x-0'
          }`}
        />
      </button>
    </div>
  );
}
