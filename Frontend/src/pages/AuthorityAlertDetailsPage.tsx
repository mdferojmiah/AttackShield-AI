import React from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import {
  HiArrowLeft,
  HiExclamationTriangle,
  HiMapPin,
  HiCalendarDays,
  HiVideoCamera,
  HiUser,
  HiShieldCheck,
  HiCheckCircle,
  HiXCircle,
  HiPhoto,
} from 'react-icons/hi2';
import { AlertsAPI } from '@/services/api';
import { useDocumentTitle } from '@/hooks';
import { formatTimeAgo, getPriorityClasses } from '@/utils/helpers';
import type { AuthorityAlert } from '@/types';
import toast from 'react-hot-toast';

export default function AuthorityAlertDetailsPage() {
  useDocumentTitle('Alert Details');

  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const alert = (location.state as { alert?: AuthorityAlert })?.alert;

  if (!alert) {
    return (
      <div className="p-8 text-center">
        <p className="text-slate-400">Alert not found.</p>
        <button
          onClick={() => navigate('/authority/dashboard')}
          className="btn-primary mt-4"
        >
          Back to Dashboard
        </button>
      </div>
    );
  }

  const handleAccept = async () => {
    try {
      const res = await AlertsAPI.accept(alert._id);
      if (res.success) {
        toast.success('Alert accepted');
        navigate('/authority/dashboard');
      }
    } catch {
      toast.error('Failed to accept alert');
    }
  };

  const handleDismiss = async () => {
    try {
      const res = await AlertsAPI.dismiss(alert._id);
      if (res.success) {
        toast.success('Alert dismissed');
        navigate('/authority/dashboard');
      }
    } catch {
      toast.error('Failed to dismiss alert');
    }
  };

  const priorityClasses = getPriorityClasses(alert.priority);

  return (
    <div className="p-4 lg:p-8 max-w-2xl mx-auto space-y-6">
      {/* Back */}
      <button
        onClick={() => navigate(-1)}
        className="flex items-center gap-2 text-primary hover:underline text-sm"
      >
        <HiArrowLeft size={18} />
        Back
      </button>

      {/* Header */}
      <div className="space-y-2">
        <div className="flex items-center gap-3 flex-wrap">
          <HiExclamationTriangle className="text-red-400" size={28} />
          <h1 className="text-2xl font-bold text-white">
            {alert.weaponType || alert.incidentType || 'Weapon Detected'}
          </h1>
          <span
            className={`text-xs font-bold uppercase px-3 py-1 rounded-full ${priorityClasses}`}
          >
            {alert.priority}
          </span>
        </div>
        <p className="text-xs text-slate-500">ID: {alert._id}</p>
      </div>

      {/* Image */}
      {alert.imageUrl && (
        <div className="card overflow-hidden p-0">
          <img
            src={alert.imageUrl}
            alt="Detection"
            className="w-full max-h-80 object-contain bg-black"
          />
        </div>
      )}
      {!alert.imageUrl && (
        <div className="card flex items-center justify-center py-16 bg-dark-surface">
          <HiPhoto className="text-slate-600" size={56} />
        </div>
      )}

      {/* Metadata Grid */}
      <div className="grid grid-cols-2 gap-3">
        <MetaCard
          icon={<HiMapPin className="text-primary" size={18} />}
          label="Location"
          value={alert.location || 'Unknown'}
        />
        <MetaCard
          icon={<HiCalendarDays className="text-primary" size={18} />}
          label="Time"
          value={formatTimeAgo(alert.createdAt)}
        />
        <MetaCard
          icon={<HiVideoCamera className="text-primary" size={18} />}
          label="Camera"
          value={alert.cameraName || 'N/A'}
        />
        <MetaCard
          icon={<HiShieldCheck className="text-primary" size={18} />}
          label="Confidence"
          value={
            alert.confidence != null
              ? `${(alert.confidence * 100).toFixed(1)}%`
              : 'N/A'
          }
        />
        {alert.reportedBy && (
          <MetaCard
            icon={<HiUser className="text-primary" size={18} />}
            label="Reported By"
            value={alert.reportedBy}
          />
        )}
        {alert.status && (
          <MetaCard
            icon={<HiShieldCheck className="text-primary" size={18} />}
            label="Status"
            value={alert.status}
          />
        )}
      </div>

      {/* Description */}
      {alert.description && (
        <div className="card">
          <h3 className="text-sm font-semibold text-slate-300 mb-2">
            Details
          </h3>
          <p className="text-slate-400 text-sm leading-relaxed">
            {alert.description}
          </p>
        </div>
      )}

      {/* Actions */}
      {alert.status === 'pending' && (
        <div className="flex gap-3">
          <button
            onClick={handleAccept}
            className="flex-1 btn-primary flex items-center justify-center gap-2"
          >
            <HiCheckCircle size={20} />
            Accept
          </button>
          <button
            onClick={handleDismiss}
            className="flex-1 btn-danger flex items-center justify-center gap-2"
          >
            <HiXCircle size={20} />
            Dismiss
          </button>
        </div>
      )}
    </div>
  );
}

function MetaCard({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
}) {
  return (
    <div className="card flex items-start gap-3 py-3">
      <span className="flex-shrink-0 mt-0.5">{icon}</span>
      <div className="min-w-0">
        <p className="text-[10px] text-slate-500 uppercase tracking-wider">
          {label}
        </p>
        <p className="text-sm font-medium text-white truncate">{value}</p>
      </div>
    </div>
  );
}
