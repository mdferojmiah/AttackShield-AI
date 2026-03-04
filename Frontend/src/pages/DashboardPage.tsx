/**
 * Dashboard Page (User)
 * AttackShield AI – Ensemble Detection Dashboard
 */

import React, { useState, useEffect, useCallback } from 'react';
import {
  HiExclamationCircle,
  HiBell,
  HiChartBar,
  HiShieldCheck,
  HiExclamationTriangle,
  HiArrowRightOnRectangle,
  HiArrowPath,
  HiCloudArrowDown,
  HiEye,
  HiUserGroup,
  HiCpuChip,
} from 'react-icons/hi2';
import { motion } from 'framer-motion';
import { DashboardAPI } from '@/services/api';
import type { DashboardStats, Activity } from '@/types';
import { useAuth } from '@/context';
import { useDocumentTitle } from '@/hooks';
import { LoadingSpinner, StatsCard } from '@/components';

export default function DashboardPage() {
  useDocumentTitle('Dashboard');
  const { user, logout } = useAuth();

  const [stats, setStats] = useState<DashboardStats>({
    totalWeapons: 0,
    alertsSent: 0,
    accuracy: 0,
    suspiciousActivities: 0,
    facesDetected: 0,
    uniquePersons: 0,
    trustScore: 92,
    ensembleConfidence: 0,
  });
  const [activities, setActivities] = useState<Activity[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchData = useCallback(async () => {
    try {
      setError(null);
      const [statsRes, actRes] = await Promise.all([
        DashboardAPI.getStats(),
        DashboardAPI.getActivity(),
      ]);

      if (statsRes.success && statsRes.data) setStats(statsRes.data);
      if (actRes.success && actRes.data) setActivities(actRes.data);

      if (!statsRes.success && !actRes.success) {
        setError('Could not reach backend — please ensure the server is running');
      }
    } catch {
      setError('Failed to load dashboard data — ensure Backend & AI Service are running');
    } finally {
      setLoading(false);
    }
  }, []);

  // Initial load + auto-refresh every 10 seconds for real-time data
  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, 10000);
    return () => clearInterval(interval);
  }, [fetchData]);

  const getActivityStyle = (type: string) => {
    switch (type) {
      case 'high':
        return { bg: 'bg-red-500/10', text: 'text-red-400', icon: <HiExclamationTriangle size={20} />, label: 'High Risk Alert' };
      case 'medium':
        return { bg: 'bg-blue-500/10', text: 'text-blue-400', icon: <HiExclamationCircle size={20} />, label: 'Medium Risk Alert' };
      default:
        return { bg: 'bg-emerald-500/10', text: 'text-emerald-400', icon: <HiShieldCheck size={20} />, label: 'Normal Status' };
    }
  };

  if (loading) return <LoadingSpinner text="Loading Dashboard..." />;

  return (
    <div className="p-4 lg:p-8 max-w-6xl mx-auto space-y-8">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <p className="text-sm text-slate-500 dark:text-slate-400">Welcome,</p>
          <h2 className="text-xl font-bold text-slate-800 dark:text-white">{user?.name || 'User'}</h2>
        </div>
        <div className="flex items-center gap-3">
          <button onClick={fetchData} className="p-2 text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-white rounded-lg hover:bg-light-surface dark:hover:bg-dark-elevated transition-colors" title="Refresh">
            <HiArrowPath size={20} />
          </button>
        </div>
      </div>

      {/* Primary KPI Cards */}
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4 }}
        className="grid grid-cols-1 md:grid-cols-3 gap-4"
      >
        <StatsCard
          icon={<HiExclamationCircle className="text-white" size={28} />}
          value={stats.totalWeapons}
          label="Detected Weapons"
          className="bg-gradient-to-br from-blue-900/60 to-blue-800/40 border border-blue-500/20"
        />
        <StatsCard
          icon={<HiBell className="text-white" size={28} />}
          value={stats.alertsSent}
          label="Alerts Sent"
          className="bg-gradient-to-br from-red-900/60 to-red-800/40 border border-red-500/20"
        />
        <StatsCard
          icon={<HiChartBar className="text-white" size={28} />}
          value={`${Math.round(stats.accuracy * 100)}%`}
          label="Detection Accuracy"
          className="bg-gradient-to-br from-slate-800/60 to-slate-700/40 border border-slate-500/20"
        />
      </motion.div>

      {/* Ensemble Metrics Cards */}
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.4, delay: 0.15 }}
        className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4"
      >
        <StatsCard
          icon={<HiEye className="text-white" size={24} />}
          value={stats.suspiciousActivities}
          label="Suspicious Activities"
          className="bg-gradient-to-br from-amber-900/50 to-amber-800/30 border border-amber-500/20"
        />
        <StatsCard
          icon={<HiUserGroup className="text-white" size={24} />}
          value={stats.uniquePersons}
          label="Persons Detected"
          className="bg-gradient-to-br from-purple-900/50 to-purple-800/30 border border-purple-500/20"
        />
        <StatsCard
          icon={<HiShieldCheck className="text-white" size={24} />}
          value={`${stats.trustScore}%`}
          label="Trust Score"
          className="bg-gradient-to-br from-emerald-900/50 to-emerald-800/30 border border-emerald-500/20"
        />
        <StatsCard
          icon={<HiCpuChip className="text-white" size={24} />}
          value={`${Math.round(stats.ensembleConfidence * 100)}%`}
          label="Ensemble Confidence"
          className="bg-gradient-to-br from-cyan-900/50 to-cyan-800/30 border border-cyan-500/20"
        />
      </motion.div>

      {/* Recent Activity */}
      <section>
        <h3 className="text-lg font-semibold text-slate-800 dark:text-white mb-4">Recent Activity</h3>

        {activities.length === 0 ? (
          <div className="text-center py-12">
            <HiShieldCheck className="mx-auto text-emerald-400" size={48} />
            <p className="text-slate-400 mt-3">No recent activity</p>
          </div>
        ) : (
          <div className="space-y-3">
            {activities.map((activity) => {
              const style = getActivityStyle(activity.type);
              return (
                <motion.div
                  key={activity.id}
                  initial={{ opacity: 0, x: -10 }}
                  animate={{ opacity: 1, x: 0 }}
                  className={`flex items-start gap-4 rounded-xl p-4 ${style.bg}`}
                >
                  <div className={`w-10 h-10 rounded-xl flex items-center justify-center ${style.text} bg-white/5`}>
                    {style.icon}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center justify-between">
                      <span className={`text-sm font-semibold ${style.text}`}>{style.label}</span>
                      <span className="text-xs text-slate-500">{activity.time}</span>
                    </div>
                    <p className="text-sm text-slate-600 dark:text-slate-300 mt-0.5">{activity.message}</p>
                  </div>
                </motion.div>
              );
            })}
          </div>
        )}
      </section>

      {/* Error Banner */}
      {error && (
        <div className="flex items-center gap-2 bg-red-500/10 border border-red-500/20 rounded-xl p-3">
          <HiCloudArrowDown className="text-red-400" size={18} />
          <span className="text-sm text-red-400">{error}</span>
        </div>
      )}
    </div>
  );
}
