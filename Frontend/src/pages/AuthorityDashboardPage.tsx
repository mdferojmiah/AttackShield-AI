import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  HiArrowPath,
  HiExclamationTriangle,
  HiShieldExclamation,
  HiMapPin,
  HiCalendarDays,
  HiCheckCircle,
  HiXCircle,
  HiClock,
  HiFunnel,
} from 'react-icons/hi2';
import { AlertsAPI } from '@/services/api';
import { useSocket, useAuth } from '@/context';
import { useDocumentTitle } from '@/hooks';
import { LoadingSpinner } from '@/components';
import { formatTimeAgo, getPriorityClasses } from '@/utils/helpers';
import type { AuthorityAlert } from '@/types';
import toast from 'react-hot-toast';
import { motion, AnimatePresence } from 'framer-motion';

type Tab = 'new' | 'my';

export default function AuthorityDashboardPage() {
  useDocumentTitle('Authority Dashboard');

  const navigate = useNavigate();
  const { user } = useAuth();
  const { socket } = useSocket();

  const [tab, setTab] = useState<Tab>('new');
  const [alerts, setAlerts] = useState<AuthorityAlert[]>([]);
  const [loading, setLoading] = useState(true);

  const fetchAlerts = useCallback(async () => {
    setLoading(true);
    try {
      const res =
        tab === 'new'
          ? await AlertsAPI.getNew()
          : await AlertsAPI.getMyActive();
      if (res.success && res.data) {
        setAlerts(res.data);
      }
    } catch {
      toast.error('Failed to load alerts');
    } finally {
      setLoading(false);
    }
  }, [tab]);

  useEffect(() => {
    fetchAlerts();
  }, [fetchAlerts]);

  // Real-time new alerts
  useEffect(() => {
    if (!socket) return;
    const handler = (data: AuthorityAlert) => {
      if (tab === 'new') {
        setAlerts((prev) => [data, ...prev]);
      }
      toast('New alert received', { icon: '🚨' });
    };
    socket.on('new-alert', handler);
    return () => {
      socket.off('new-alert', handler);
    };
  }, [socket, tab]);

  const handleAccept = async (alertId: string) => {
    try {
      const res = await AlertsAPI.accept(alertId);
      if (res.success) {
        toast.success('Alert accepted');
        setAlerts((prev) => prev.filter((a) => a._id !== alertId));
      }
    } catch {
      toast.error('Failed to accept alert');
    }
  };

  const handleDismiss = async (alertId: string) => {
    try {
      const res = await AlertsAPI.dismiss(alertId);
      if (res.success) {
        toast.success('Alert dismissed');
        setAlerts((prev) => prev.filter((a) => a._id !== alertId));
      }
    } catch {
      toast.error('Failed to dismiss alert');
    }
  };

  return (
    <div className="p-4 lg:p-8 max-w-4xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-xl font-bold text-white">Alert Dashboard</h2>
          <p className="text-sm text-slate-400">
            {user?.name || 'Authority'}
          </p>
        </div>
        <button
          onClick={fetchAlerts}
          className="p-2 text-primary hover:bg-primary/10 rounded-lg transition-colors"
        >
          <HiArrowPath size={22} />
        </button>
      </div>

      {/* Tabs */}
      <div className="flex bg-dark-card rounded-xl p-1">
        {(['new', 'my'] as Tab[]).map((t) => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`flex-1 py-2.5 rounded-lg text-sm font-bold uppercase tracking-wide transition-colors ${
              tab === t
                ? 'bg-accent text-white'
                : 'text-slate-400 hover:text-white'
            }`}
          >
            {t === 'new' ? 'New Alerts' : 'My Alerts'}
          </button>
        ))}
      </div>

      {/* Alert List */}
      {loading ? (
        <LoadingSpinner text="Loading Alerts..." />
      ) : alerts.length === 0 ? (
        <div className="text-center py-20">
          <HiShieldExclamation className="mx-auto text-slate-500" size={52} />
          <p className="text-slate-400 mt-4">
            {tab === 'new' ? 'No new alerts' : 'No accepted alerts'}
          </p>
        </div>
      ) : (
        <AnimatePresence>
          <ul className="space-y-3">
            {alerts.map((alert) => (
              <motion.li
                key={alert._id}
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, x: -100 }}
                className="card cursor-pointer hover:bg-dark-surface transition-colors"
                onClick={() =>
                  navigate(`/authority/alerts/${alert._id}`, {
                    state: { alert },
                  })
                }
              >
                <div className="flex items-start gap-3">
                  {/* Priority Dot */}
                  <span
                    className={`mt-1 w-3 h-3 rounded-full flex-shrink-0 ${getPriorityClasses(alert.priority).split(' ')[0]}`}
                  />

                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <HiExclamationTriangle className="text-red-400" size={16} />
                      <span className="font-bold text-white text-sm">
                        {alert.weaponType || alert.incidentType || 'Weapon Detected'}
                      </span>
                      <span
                        className={`text-[10px] font-bold uppercase px-2 py-0.5 rounded-full ${getPriorityClasses(alert.priority)}`}
                      >
                        {alert.priority}
                      </span>
                    </div>
                    <p className="text-xs text-slate-400 mt-1 flex items-center gap-1 truncate">
                      <HiMapPin size={12} /> {alert.location || 'Unknown'}
                    </p>
                    <p className="text-[10px] text-slate-500 mt-0.5 flex items-center gap-1">
                      <HiCalendarDays size={12} />{' '}
                      {formatTimeAgo(alert.createdAt)}
                    </p>

                    {/* Confidence */}
                    {alert.confidence != null && (
                      <div className="mt-2">
                        <div className="h-1.5 bg-dark-surface rounded-full overflow-hidden">
                          <div
                            className="h-full bg-accent rounded-full"
                            style={{ width: `${Math.round(alert.confidence * 100)}%` }}
                          />
                        </div>
                        <p className="text-[10px] text-slate-500 mt-0.5">
                          Confidence: {(alert.confidence * 100).toFixed(1)}%
                        </p>
                      </div>
                    )}
                  </div>

                  {/* Actions */}
                  {tab === 'new' && (
                    <div className="flex gap-2 flex-shrink-0">
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          handleAccept(alert._id);
                        }}
                        className="p-2 bg-emerald-500/20 text-emerald-400 rounded-lg hover:bg-emerald-500/30"
                        title="Accept"
                      >
                        <HiCheckCircle size={20} />
                      </button>
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          handleDismiss(alert._id);
                        }}
                        className="p-2 bg-red-500/20 text-red-400 rounded-lg hover:bg-red-500/30"
                        title="Dismiss"
                      >
                        <HiXCircle size={20} />
                      </button>
                    </div>
                  )}
                </div>
              </motion.li>
            ))}
          </ul>
        </AnimatePresence>
      )}
    </div>
  );
}
