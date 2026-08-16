/**
 * Stats Card Component
 */

import React, { type ReactNode } from 'react';

interface StatsCardProps {
  icon: ReactNode;
  value: string | number;
  label: string;
  className?: string;
}

export default function StatsCard({
  icon,
  value,
  label,
  className = '',
}: StatsCardProps) {
  return (
    <div
      className={`rounded-2xl p-5 flex items-center gap-4 shadow-lg ${className}`}
    >
      <div className="flex-shrink-0">{icon}</div>
      <div>
        <p className="text-3xl font-bold text-slate-800 dark:text-white">{value}</p>
        <p className="text-sm text-slate-600 dark:text-slate-300">{label}</p>
      </div>
    </div>
  );
}
