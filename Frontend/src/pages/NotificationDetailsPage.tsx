import React, { useState, useEffect } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  HiArrowLeft,
  HiExclamationTriangle,
  HiInformationCircle,
  HiCheckCircle,
  HiBell,
  HiCalendarDays,
  HiMapPin,
  HiVideoCamera,
} from 'react-icons/hi2';
import { NotificationsAPI } from '@/services/api';
import { useDocumentTitle } from '@/hooks';
import { LoadingSpinner } from '@/components';
import { formatTimeAgo } from '@/utils/helpers';
import type { NotificationItem } from '@/types';

const TYPE_STYLES: Record<string, { bg: string; text: string; icon: React.ReactNode }> = {
  weapon: {
    bg: 'bg-red-500/20',
    text: 'text-red-400',
    icon: <HiExclamationTriangle size={18} />,
  },
  hit_list: {
    bg: 'bg-red-500/20',
    text: 'text-red-400',
    icon: <HiExclamationTriangle size={18} />,
  },
  suspicious: {
    bg: 'bg-yellow-500/20',
    text: 'text-yellow-400',
    icon: <HiExclamationTriangle size={18} />,
  },
  person: {
    bg: 'bg-emerald-500/20',
    text: 'text-emerald-400',
    icon: <HiCheckCircle size={18} />,
  },
  face: {
    bg: 'bg-emerald-500/20',
    text: 'text-emerald-400',
    icon: <HiCheckCircle size={18} />,
  },
  alert: {
    bg: 'bg-red-500/20',
    text: 'text-red-400',
    icon: <HiExclamationTriangle size={18} />,
  },
  warning: {
    bg: 'bg-yellow-500/20',
    text: 'text-yellow-400',
    icon: <HiExclamationTriangle size={18} />,
  },
  info: {
    bg: 'bg-blue-500/20',
    text: 'text-blue-400',
    icon: <HiInformationCircle size={18} />,
  },
  success: {
    bg: 'bg-emerald-500/20',
    text: 'text-emerald-400',
    icon: <HiCheckCircle size={18} />,
  },
};

export default function NotificationDetailsPage() {
  useDocumentTitle('Notification Details');

  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const cached = (location.state as { notification?: NotificationItem })
    ?.notification;

  const [notification, setNotification] = useState<NotificationItem | null>(
    cached ?? null,
  );
  // The list response omits imageUrl to keep pages small, so even a cached row must be refetched.
  const hasFullRow = Boolean(cached?.imageUrl);
  const hadCache = Boolean(cached);
  const [loading, setLoading] = useState(!hasFullRow);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!id || hasFullRow) return;

    let active = true;
    setLoading(true);
    NotificationsAPI.getById(id)
      .then((res) => {
        if (!active) return;
        if (res.success && res.data) setNotification(res.data);
        else if (!hadCache) setError(res.error || 'Notification not found');
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, [id, hasFullRow, hadCache]);

  if (loading && !notification) return <LoadingSpinner text="Loading Notification..." />;

  if (!notification) {
    return (
      <div className="p-8 text-center">
        <p className="text-slate-400">{error || 'Notification not found.'}</p>
        <button
          onClick={() => navigate('/notifications')}
          className="btn-primary mt-4"
        >
          Back to Notifications
        </button>
      </div>
    );
  }

  const style = TYPE_STYLES[notification.type] || {
    bg: 'bg-primary/20',
    text: 'text-primary',
    icon: <HiBell size={18} />,
  };

  return (
    <div className="p-4 lg:p-8 max-w-2xl mx-auto space-y-6">
      {/* Back Button */}
      <button
        onClick={() => navigate(-1)}
        className="flex items-center gap-2 text-primary hover:underline text-sm"
      >
        <HiArrowLeft size={18} />
        Back
      </button>

      {/* Header */}
      <div className="space-y-2">
        <div className="flex items-center gap-3">
          <span
            className={`inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-bold uppercase ${style.bg} ${style.text}`}
          >
            {style.icon}
            {notification.type}
          </span>
        </div>
        <h1 className="text-2xl font-bold text-white">{notification.title}</h1>
        <div className="flex flex-wrap gap-4 text-xs text-slate-400">
          <span className="flex items-center gap-1">
            <HiCalendarDays size={14} />
            {formatTimeAgo(notification.createdAt)}
          </span>
          {notification.location && (
            <span className="flex items-center gap-1">
              <HiMapPin size={14} />
              {notification.location}
            </span>
          )}
          {notification.cameraName && (
            <span className="flex items-center gap-1">
              <HiVideoCamera size={14} />
              {notification.cameraName}
            </span>
          )}
        </div>
      </div>

      {notification.imageUrl && (
        <div className="card overflow-hidden p-0">
          <img
            src={notification.imageUrl}
            alt="Detection screenshot"
            className="w-full max-h-96 object-contain bg-black"
          />
        </div>
      )}

      {/* Description Card */}
      <div className="card">
        <h3 className="text-sm font-semibold text-slate-300 mb-2">
          Description
        </h3>
        <p className="text-slate-400 text-sm leading-relaxed">
          {notification.description || notification.message || 'No additional details.'}
        </p>
      </div>

      {/* Alert Info */}
      {notification.alertId && (
        <div className="card">
          <h3 className="text-sm font-semibold text-slate-300 mb-2">
            Related Alert
          </h3>
          <p className="text-slate-400 text-sm">
            Alert ID: {notification.alertId}
          </p>
        </div>
      )}
    </div>
  );
}
