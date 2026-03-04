import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  HiArrowPath,
  HiMagnifyingGlass,
  HiClock,
  HiExclamationTriangle,
  HiMapPin,
  HiCalendarDays,
  HiFunnel,
  HiXMark,
  HiChevronDown,
  HiChevronRight,
} from 'react-icons/hi2';
import { AlertsAPI } from '@/services/api';
import { useDocumentTitle } from '@/hooks';
import { LoadingSpinner } from '@/components';
import { formatTimeAgo, getPriorityClasses } from '@/utils/helpers';
import type { AuthorityAlert } from '@/types';
import toast from 'react-hot-toast';

const INCIDENT_TYPES = [
  'All',
  'Knife',
  'Gun',
  'Rifle',
  'Handgun',
  'Unknown',
];

export default function AuthorityHistoryPage() {
  useDocumentTitle('Alert History');

  const navigate = useNavigate();

  const [alerts, setAlerts] = useState<AuthorityAlert[]>([]);
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState('');
  const [showFilters, setShowFilters] = useState(false);
  const [selectedType, setSelectedType] = useState('All');
  const [dateFrom, setDateFrom] = useState('');
  const [dateTo, setDateTo] = useState('');

  const fetchHistory = useCallback(async () => {
    setLoading(true);
    try {
      const res = await AlertsAPI.getHistory();
      if (res.success && res.data) {
        setAlerts(res.data);
      }
    } catch {
      toast.error('Failed to load history');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchHistory();
  }, [fetchHistory]);

  // Filter logic
  const filteredAlerts = alerts.filter((a) => {
    const q = searchQuery.toLowerCase();
    const matchesSearch =
      !q ||
      (a.weaponType || a.incidentType || '').toLowerCase().includes(q) ||
      (a.location || '').toLowerCase().includes(q) ||
      (a.cameraName || '').toLowerCase().includes(q);

    const matchesType =
      selectedType === 'All' ||
      (a.weaponType || a.incidentType || '').toLowerCase() ===
        selectedType.toLowerCase();

    let matchesDate = true;
    if (dateFrom) {
      matchesDate =
        matchesDate && new Date(a.createdAt) >= new Date(dateFrom);
    }
    if (dateTo) {
      const to = new Date(dateTo);
      to.setHours(23, 59, 59, 999);
      matchesDate = matchesDate && new Date(a.createdAt) <= to;
    }

    return matchesSearch && matchesType && matchesDate;
  });

  // Group by date
  const grouped: Record<string, AuthorityAlert[]> = {};
  filteredAlerts.forEach((a) => {
    const day = new Date(a.createdAt).toLocaleDateString('en-GB', {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
    (grouped[day] ||= []).push(a);
  });

  const clearFilters = () => {
    setSelectedType('All');
    setDateFrom('');
    setDateTo('');
    setSearchQuery('');
  };

  return (
    <div className="p-4 lg:p-8 max-w-4xl mx-auto space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-bold text-white">Alert History</h2>
        <div className="flex gap-2">
          <button
            onClick={() => setShowFilters((f) => !f)}
            className="p-2 text-primary hover:bg-primary/10 rounded-lg transition-colors"
          >
            <HiFunnel size={22} />
          </button>
          <button
            onClick={fetchHistory}
            className="p-2 text-primary hover:bg-primary/10 rounded-lg transition-colors"
          >
            <HiArrowPath size={22} />
          </button>
        </div>
      </div>

      {/* Search */}
      <div className="relative">
        <HiMagnifyingGlass className="absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" size={18} />
        <input
          type="text"
          placeholder="Search by weapon, location, camera..."
          className="input-field pl-10"
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
        />
      </div>

      {/* Filters */}
      {showFilters && (
        <div className="card space-y-4">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-bold text-slate-300">Filters</h3>
            <button onClick={clearFilters} className="text-xs text-accent hover:underline">
              Clear all
            </button>
          </div>

          {/* Incident Type */}
          <div>
            <label className="text-xs text-slate-400 block mb-1.5">
              Incident Type
            </label>
            <div className="flex flex-wrap gap-2">
              {INCIDENT_TYPES.map((t) => (
                <button
                  key={t}
                  onClick={() => setSelectedType(t)}
                  className={`px-3 py-1 rounded-full text-xs font-bold transition-colors ${
                    selectedType === t
                      ? 'bg-accent text-white'
                      : 'bg-dark-surface text-slate-400 hover:text-white'
                  }`}
                >
                  {t}
                </button>
              ))}
            </div>
          </div>

          {/* Date Range */}
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="text-xs text-slate-400 block mb-1">From</label>
              <input
                type="date"
                className="input-field text-sm"
                value={dateFrom}
                onChange={(e) => setDateFrom(e.target.value)}
              />
            </div>
            <div>
              <label className="text-xs text-slate-400 block mb-1">To</label>
              <input
                type="date"
                className="input-field text-sm"
                value={dateTo}
                onChange={(e) => setDateTo(e.target.value)}
              />
            </div>
          </div>
        </div>
      )}

      {/* Results */}
      {loading ? (
        <LoadingSpinner text="Loading History..." />
      ) : filteredAlerts.length === 0 ? (
        <div className="text-center py-20">
          <HiClock className="mx-auto text-slate-500" size={52} />
          <p className="text-slate-400 mt-4">No alerts found</p>
        </div>
      ) : (
        <div className="space-y-6">
          {Object.entries(grouped).map(([date, items]) => (
            <section key={date}>
              <h3 className="text-xs font-bold text-slate-500 uppercase tracking-wider mb-2">
                {date}
              </h3>
              <ul className="space-y-2">
                {items.map((a) => (
                  <li
                    key={a._id}
                    onClick={() =>
                      navigate(`/authority/alerts/${a._id}`, {
                        state: { alert: a },
                      })
                    }
                    className="card flex items-center gap-3 cursor-pointer hover:bg-dark-surface transition-colors"
                  >
                    <span
                      className={`w-2.5 h-2.5 rounded-full flex-shrink-0 ${getPriorityClasses(a.priority).split(' ')[0]}`}
                    />
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <HiExclamationTriangle className="text-red-400 flex-shrink-0" size={14} />
                        <span className="font-semibold text-white text-sm truncate">
                          {a.weaponType || a.incidentType || 'Detection'}
                        </span>
                        {/* Status pill */}
                        <span
                          className={`text-[10px] font-bold uppercase px-2 py-0.5 rounded-full ${
                            a.status === 'accepted'
                              ? 'bg-emerald-500/20 text-emerald-400'
                              : a.status === 'dismissed'
                                ? 'bg-red-500/20 text-red-400'
                                : 'bg-yellow-500/20 text-yellow-400'
                          }`}
                        >
                          {a.status}
                        </span>
                      </div>
                      <p className="text-xs text-slate-400 truncate flex items-center gap-1 mt-0.5">
                        <HiMapPin size={12} /> {a.location || 'Unknown'}
                        <span className="text-slate-600 mx-1">·</span>
                        {formatTimeAgo(a.createdAt)}
                      </p>
                    </div>
                    <HiChevronRight className="text-slate-500 flex-shrink-0" size={16} />
                  </li>
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}
