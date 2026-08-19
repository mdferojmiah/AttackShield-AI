/**
 * Email cooldown context
 * Tracks the server-side throttle that spaces out alert emails, so the navbar can
 * count down to the next possible send and the user learns why one was skipped.
 *
 * Lives in a provider rather than a hook because both navbars render the badge and
 * the "throttled" toast must fire exactly once per event.
 */

import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from 'react';
import toast from 'react-hot-toast';
import { SettingsAPI } from '@/services/api';
import type { EmailCooldown } from '@/types';
import { useAuth } from './AuthContext';
import { useSocket } from './SocketContext';

interface EmailCooldownContextType {
  /** True while an email is being withheld by the throttle. */
  active: boolean;
  /** Milliseconds until the next email may be sent; 0 when idle. */
  remainingMs: number;
  /** "m:ss" countdown, or null when idle. */
  remainingLabel: string | null;
  /** Alert type that opened the current window (e.g. "weapon"). */
  alertType: string | null;
  cooldownMinutes: number;
  /** Re-read the throttle from the server, e.g. after toggling email alerts. */
  refresh: () => void;
}

const EmailCooldownContext = createContext<EmailCooldownContextType | undefined>(undefined);

function toEpoch(value?: string | null): number | null {
  if (!value) return null;
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? null : parsed;
}

function format(ms: number): string {
  const total = Math.ceil(ms / 1000);
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}

export function EmailCooldownProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  const { socket, connectionEpoch } = useSocket();

  const [nextAllowedAt, setNextAllowedAt] = useState<number | null>(null);
  const [alertType, setAlertType] = useState<string | null>(null);
  const [cooldownMinutes, setCooldownMinutes] = useState(0);
  const [now, setNow] = useState(() => Date.now());

  const refresh = useCallback(() => {
    if (!isAuthenticated) return;
    void SettingsAPI.getEmailCooldown().then((res) => {
      const data = res.success ? res.data?.data : undefined;
      if (!data) return;
      setCooldownMinutes(data.cooldownMinutes);
      setNextAllowedAt(data.enabled ? toEpoch(data.nextAllowedAt) : null);
      setAlertType(data.alertType ?? null);
    });
  }, [isAuthenticated]);

  // Restore the countdown after a reload, and re-sync on reconnect since the
  // throttle lives in server memory and resets when the API restarts.
  useEffect(() => {
    if (!isAuthenticated) {
      setNextAllowedAt(null);
      setAlertType(null);
      return;
    }
    refresh();
  }, [isAuthenticated, connectionEpoch, refresh]);

  useEffect(() => {
    if (!socket) return;

    const handler = (payload: EmailCooldown) => {
      setCooldownMinutes(payload.cooldownMinutes);
      setNextAllowedAt(toEpoch(payload.nextAllowedAt));
      setAlertType(payload.alertType ?? null);

      if (payload.sent === false) {
        toast(
          `Email alert skipped — we space alert emails ${payload.cooldownMinutes} minutes apart to stay within the daily mail limit. Next one in ${format(
            Math.max((toEpoch(payload.nextAllowedAt) ?? Date.now()) - Date.now(), 0),
          )}.`,
          { icon: '📪', duration: 6000, id: 'email-cooldown' },
        );
      }
    };

    socket.on('email-cooldown', handler);
    return () => socket.off('email-cooldown', handler);
  }, [socket]);

  // Tick only while counting down so an idle app does no work.
  const active = nextAllowedAt !== null && nextAllowedAt > now;
  useEffect(() => {
    if (!active) return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [active]);

  // A window that just elapsed leaves stale state behind; clear it.
  useEffect(() => {
    if (nextAllowedAt !== null && nextAllowedAt <= now) {
      setNextAllowedAt(null);
      setAlertType(null);
    }
  }, [nextAllowedAt, now]);

  const remainingMs = active ? nextAllowedAt! - now : 0;

  return (
    <EmailCooldownContext.Provider
      value={{
        active,
        remainingMs,
        remainingLabel: active ? format(remainingMs) : null,
        alertType,
        cooldownMinutes,
        refresh,
      }}
    >
      {children}
    </EmailCooldownContext.Provider>
  );
}

export function useEmailCooldown() {
  const context = useContext(EmailCooldownContext);
  if (context === undefined) {
    throw new Error('useEmailCooldown must be used within an EmailCooldownProvider');
  }
  return context;
}
