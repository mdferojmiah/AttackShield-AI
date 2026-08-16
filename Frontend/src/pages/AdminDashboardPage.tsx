import React, { useEffect, useState } from 'react';
import { HiArrowPath, HiUsers, HiVideoCamera, HiShieldCheck } from 'react-icons/hi2';
import { AdminAPI } from '@/services/api';
import type { AdminOverview } from '@/types';
import { LoadingSpinner, StatsCard } from '@/components';
import { useDocumentTitle } from '@/hooks';

export default function AdminDashboardPage() {
  useDocumentTitle('Administration');
  const [overview, setOverview] = useState<AdminOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setError(null);
    const response = await AdminAPI.getOverview();
    if (response.success && response.data) setOverview(response.data);
    else setError(response.error || 'Failed to load administration data');
    setLoading(false);
  };

  useEffect(() => { load(); }, []);

  if (loading) return <LoadingSpinner text="Loading administration..." />;

  return (
    <div className="p-4 lg:p-8 max-w-7xl mx-auto space-y-8">
      <header className="flex items-center justify-between">
        <div>
          <p className="text-sm text-slate-500 dark:text-slate-400">System control</p>
          <h2 className="text-xl font-bold text-slate-800 dark:text-white">Administration</h2>
        </div>
        <button onClick={load} className="p-2 rounded-lg text-slate-500 hover:text-primary" title="Refresh">
          <HiArrowPath size={20} />
        </button>
      </header>

      {error && <div className="p-3 rounded-lg bg-red-500/10 text-red-500">{error}</div>}

      {overview && (
        <>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
            <StatsCard icon={<HiUsers size={26} />} value={overview.totalUsers} label="Users" />
            <StatsCard icon={<HiShieldCheck size={26} />} value={overview.activeUsers} label="Active Accounts" />
            <StatsCard icon={<HiVideoCamera size={26} />} value={overview.totalCameras} label="Registered Cameras" />
          </div>

          <section>
            <h3 className="text-lg font-semibold text-slate-800 dark:text-white mb-4">Users and cameras</h3>
            <div className="overflow-x-auto border border-light-border dark:border-dark-border rounded-lg">
              <table className="w-full text-sm text-left">
                <thead className="bg-light-surface dark:bg-dark-elevated text-slate-500">
                  <tr><th className="p-3">User</th><th className="p-3">Role</th><th className="p-3">Status</th><th className="p-3">Cameras</th><th className="p-3">Last login</th></tr>
                </thead>
                <tbody className="divide-y divide-light-border dark:divide-dark-border">
                  {overview.users.map((user) => (
                    <tr key={user.id} className="text-slate-700 dark:text-slate-300 align-top">
                      <td className="p-3"><div className="font-medium text-slate-900 dark:text-white">{user.name}</div><div className="text-xs text-slate-500">{user.email}</div></td>
                      <td className="p-3 capitalize">{user.role.replace('_', ' ')}</td>
                      <td className="p-3">{user.isActive ? 'Active' : 'Disabled'}</td>
                      <td className="p-3">{user.cameras.length ? user.cameras.map(camera => <div key={camera.id}>{camera.name} <span className="text-xs text-slate-500">{camera.location || 'No location'}</span></div>) : <span className="text-slate-500">None</span>}</td>
                      <td className="p-3 whitespace-nowrap">{user.lastLogin ? new Date(user.lastLogin).toLocaleString() : 'Never'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section>
            <h3 className="text-lg font-semibold text-slate-800 dark:text-white mb-4">Recent detection activity</h3>
            <div className="overflow-x-auto border border-light-border dark:border-dark-border rounded-lg">
              <table className="w-full text-sm text-left">
                <thead className="bg-light-surface dark:bg-dark-elevated text-slate-500">
                  <tr><th className="p-3">User</th><th className="p-3">Event</th><th className="p-3">Location</th><th className="p-3">Confidence</th><th className="p-3">Time</th></tr>
                </thead>
                <tbody className="divide-y divide-light-border dark:divide-dark-border">
                  {overview.recentActions.map((action) => (
                    <tr key={action.id} className="text-slate-700 dark:text-slate-300">
                      <td className="p-3 font-medium">{action.userName}</td>
                      <td className="p-3"><span className="capitalize">{action.type.replace('_', ' ')}</span><div className="text-xs text-slate-500">{action.label}</div></td>
                      <td className="p-3">{action.location}</td>
                      <td className="p-3">{Math.round(action.confidence * 100)}%</td>
                      <td className="p-3 whitespace-nowrap">{new Date(action.createdAt).toLocaleString()}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}
    </div>
  );
}