import React from 'react';
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
import { useDocumentTitle } from '@/hooks';
import { formatTimeAgo } from '@/utils/helpers';
import type { NotificationItem } from '@/types';

const TYPE_STYLES: Record<string, { bg: string; text: string; icon: React.ReactNode }> = {
  alert: {
    bg: 'bg-red-500/20',
    text: 'text-red-400',
    icon: <HiExclamationTriangle size={18} />,
  },
  warning: {
    bg: 'bg-orange-500/20',
    text: 'text-orange-400',
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
  const notification = (location.state as { notification?: NotificationItem })
    ?.notification;

  if (!notification) {
    return (
      <div className="p-8 text-center">
        <p className="text-slate-400">Notification not found.</p>
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
