import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  HiBell,
  HiBellSlash,
  HiExclamationTriangle,
  HiInformationCircle,
  HiCheckCircle,
  HiChevronRight,
  HiArrowPath,
} from 'react-icons/hi2';
import { NotificationsAPI } from '@/services/api';
import { useSocket } from '@/context';
import { useDocumentTitle } from '@/hooks';
import { LoadingSpinner } from '@/components';
import { formatTimeAgo } from '@/utils/helpers';
import type { NotificationItem } from '@/types';
import toast from 'react-hot-toast';

const ICON_MAP: Record<string, React.ReactNode> = {
  weapon: <HiExclamationTriangle className="text-red-400" size={24} />,
  hit_list: <HiExclamationTriangle className="text-red-400" size={24} />,
  suspicious: <HiExclamationTriangle className="text-yellow-400" size={24} />,
  person: <HiCheckCircle className="text-emerald-400" size={24} />,
  face: <HiCheckCircle className="text-emerald-400" size={24} />,
  alert: <HiExclamationTriangle className="text-red-400" size={24} />,
  warning: <HiExclamationTriangle className="text-yellow-400" size={24} />,
  info: <HiInformationCircle className="text-blue-400" size={24} />,
  success: <HiCheckCircle className="text-emerald-400" size={24} />,
};

const PAGE_SIZE = 10;

export default function NotificationsPage() {
  useDocumentTitle('Notifications');

  const navigate = useNavigate();
  const { socket } = useSocket();

  const [notifications, setNotifications] = useState<NotificationItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [cursor, setCursor] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);

  const fetchFirstPage = useCallback(async () => {
    setLoading(true);
    try {
      const res = await NotificationsAPI.getPage({ limit: PAGE_SIZE });
      if (res.success && res.data) {
        setNotifications(res.data.items);
        setCursor(res.data.nextCursor);
        setHasMore(res.data.hasMore);
      }
    } catch {
      toast.error('Failed to load notifications');
    } finally {
      setLoading(false);
    }
  }, []);

  const loadMore = useCallback(async () => {
    if (!cursor || loadingMore) return;
    setLoadingMore(true);
    try {
      const res = await NotificationsAPI.getPage({ limit: PAGE_SIZE, cursor });
      if (res.success && res.data) {
        const { items, nextCursor, hasMore: more } = res.data;
        // A socket push can race an in-flight page and land the same row twice.
        setNotifications((prev) => {
          const seen = new Set(prev.map((n) => n._id));
          return [...prev, ...items.filter((n) => !seen.has(n._id))];
        });
        setCursor(nextCursor);
        setHasMore(more);
      } else {
        toast.error('Failed to load more notifications');
      }
    } catch {
      toast.error('Failed to load more notifications');
    } finally {
      setLoadingMore(false);
    }
  }, [cursor, loadingMore]);

  useEffect(() => {
    fetchFirstPage();
  }, [fetchFirstPage]);

  // Real-time notification via socket
  useEffect(() => {
    if (!socket) return;
    const handler = (data: NotificationItem) => {
      setNotifications((prev) =>
        prev.some((n) => n._id === data._id) ? prev : [data, ...prev],
      );
      toast('New notification received', { icon: '🔔' });
    };
    socket.on('notification-created', handler);
    return () => {
      socket.off('notification-created', handler);
    };
  }, [socket]);

  const markAsRead = async (id: string) => {
    try {
      await NotificationsAPI.markAsRead(id);
      setNotifications((prev) =>
        prev.map((n) => (n._id === id ? { ...n, read: true } : n)),
      );
    } catch {
      /* silent */
    }
  };

  const getIcon = (type: string) =>
    ICON_MAP[type] || <HiBell className="text-primary" size={24} />;

  if (loading) return <LoadingSpinner text="Loading Notifications..." />;

  return (
    <div className="p-4 lg:p-8 max-w-3xl mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between mb-6">
        <h2 className="text-xl font-bold text-slate-800 dark:text-white">Notifications</h2>
        <button
          onClick={fetchFirstPage}
          className="p-2 text-primary hover:bg-primary/10 rounded-lg transition-colors"
          title="Refresh"
        >
          <HiArrowPath size={22} />
        </button>
      </div>

      {notifications.length === 0 ? (
        <div className="text-center py-20">
          <HiBellSlash className="mx-auto text-slate-500" size={52} />
          <p className="text-slate-400 mt-4">No notifications yet</p>
        </div>
      ) : (
        <ul className="space-y-2">
          {notifications.map((n) => (
            <li
              key={n._id}
              onClick={() => {
                if (!n.read) markAsRead(n._id);
                navigate(`/notifications/${n._id}`, { state: { notification: n } });
              }}
              className={`card flex items-center gap-4 cursor-pointer hover:bg-light-surface dark:hover:bg-dark-surface transition-colors ${
                !n.read ? 'border-l-4 border-l-accent' : ''
              }`}
            >
              <div className="flex-shrink-0">{getIcon(n.type)}</div>
              <div className="flex-1 min-w-0">
                <p className="font-semibold text-slate-800 dark:text-white text-sm truncate">
                  {n.title}
                </p>
                <p className="text-xs text-slate-400 truncate">
                  {n.message || n.description}
                </p>
                <span className="text-[10px] text-slate-500 mt-1 inline-block">
                  {formatTimeAgo(n.createdAt)}
                </span>
              </div>
              <HiChevronRight className="text-slate-500 flex-shrink-0" size={18} />
            </li>
          ))}
        </ul>
      )}

      {notifications.length > 0 && (
        <div className="mt-6 text-center">
          {hasMore ? (
            <button
              onClick={loadMore}
              disabled={loadingMore}
              className="btn-outline inline-flex items-center gap-2"
            >
              {loadingMore && <HiArrowPath className="animate-spin" size={18} />}
              {loadingMore ? 'Loading…' : 'See more'}
            </button>
          ) : (
            <p className="text-xs text-slate-500">You&rsquo;re all caught up</p>
          )}
        </div>
      )}
    </div>
  );
}
