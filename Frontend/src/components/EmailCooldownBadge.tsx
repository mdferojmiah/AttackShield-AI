/**
 * EmailCooldownBadge
 * Counts down to the moment the next alert email can be sent. Renders nothing
 * while no throttle is active.
 */

import React from 'react';
import { HiEnvelope } from 'react-icons/hi2';
import { useEmailCooldown } from '@/context';

interface EmailCooldownBadgeProps {
  /** `compact` drops the caption for the mobile header. */
  variant?: 'full' | 'compact';
}

export default function EmailCooldownBadge({ variant = 'full' }: EmailCooldownBadgeProps) {
  const { active, remainingLabel, cooldownMinutes } = useEmailCooldown();

  if (!active || !remainingLabel) return null;

  const title = `Alert emails are spaced ${cooldownMinutes} minutes apart to stay within the daily mail limit. Next email available in ${remainingLabel}.`;

  return (
    <div
      title={title}
      className="flex items-center gap-2 px-3 py-2 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-500 mb-5"
    >
      <HiEnvelope size={16} className="shrink-0" />
      {variant === 'full' ? (
        <div className="min-w-0">
          <p className="text-[11px] leading-tight text-amber-500/80">Next email in</p>
          <p className="text-sm font-semibold leading-tight tabular-nums">{remainingLabel}</p>
        </div>
      ) : (
        <span className="text-xs font-semibold tabular-nums">{remainingLabel}</span>
      )}
    </div>
  );
}
