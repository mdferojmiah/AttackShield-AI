/**
 * Sidebar Navigation Component
 * Desktop-first sidebar with responsive collapse
 */

import React from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import {
  HiHome,
  HiVideoCamera,
  HiBell,
  HiCog6Tooth,
  HiArrowRightOnRectangle,
  HiShieldCheck,
  HiClock,
  HiGlobeAlt,
} from 'react-icons/hi2';
import { useAuth } from '@/context';

interface NavItem {
  path: string;
  label: string;
  icon: React.ReactNode;
}

export default function Sidebar() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const isAuthority = user?.role === 'authority';

  const handleLogout = () => {
    if (window.confirm('Are you sure you want to logout?')) {
      logout();
      navigate('/login');
    }
  };

  const userNav: NavItem[] = [
    { path: '/dashboard', label: 'Dashboard', icon: <HiHome size={20} /> },
    { path: '/live-feed', label: 'Live Feed', icon: <HiVideoCamera size={20} /> },
    { path: '/notifications', label: 'Notifications', icon: <HiBell size={20} /> },
    { path: '/settings', label: 'Settings', icon: <HiCog6Tooth size={20} /> },
  ];

  const authorityNav: NavItem[] = [
    { path: '/authority/dashboard', label: 'Alerts', icon: <HiShieldCheck size={20} /> },
    { path: '/authority/history', label: 'History', icon: <HiClock size={20} /> },
    { path: '/settings', label: 'Settings', icon: <HiCog6Tooth size={20} /> },
  ];

  const navItems = isAuthority ? authorityNav : userNav;

  return (
    <aside className="hidden lg:flex flex-col w-64 min-h-screen bg-light-surface dark:bg-dark-surface border-r border-light-border dark:border-dark-border transition-colors duration-200">
      {/* Logo / Brand */}
      <div className="flex items-center gap-3 px-6 py-5 border-b border-light-border dark:border-dark-border">
        <div className="w-9 h-9 rounded-lg bg-primary flex items-center justify-center">
          <HiShieldCheck className="text-white" size={20} />
        </div>
        <div>
          <h1 className="text-sm font-bold text-slate-800 dark:text-white leading-tight">
            AttackShield AI
          </h1>
          <p className="text-[11px] text-slate-500 dark:text-slate-400">Intelligent Surveillance</p>
        </div>
      </div>

      {/* Navigation Links */}
      <nav className="flex-1 px-3 py-4 space-y-1">
        {navItems.map((item) => (
          <NavLink
            key={item.path}
            to={item.path}
            className={({ isActive }) =>
              `flex items-center gap-3 px-4 py-2.5 rounded-xl text-sm font-medium transition-colors duration-150
              ${
                isActive
                  ? 'bg-primary/15 text-primary'
                  : 'text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-white hover:bg-light-surface dark:hover:bg-dark-elevated'
              }`
            }
          >
            {item.icon}
            {item.label}
          </NavLink>
        ))}
      </nav>

      {/* User profile & logout */}
      <div className="border-t border-light-border dark:border-dark-border px-4 py-4">
        <div className="flex items-center gap-3 mb-3">
          <div className="w-9 h-9 rounded-full bg-primary flex items-center justify-center text-white font-semibold text-sm">
            {user?.name?.charAt(0).toUpperCase() || 'U'}
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-sm font-medium text-slate-800 dark:text-white truncate">
              {user?.name || 'User'}
            </p>
            <p className="text-xs text-slate-500 dark:text-slate-400 truncate">
              {user?.email || ''}
            </p>
          </div>
        </div>
        <button
          onClick={handleLogout}
          className="flex items-center gap-2 w-full px-4 py-2 text-sm text-red-400 hover:bg-red-500/10 rounded-xl transition-colors"
        >
          <HiArrowRightOnRectangle size={18} />
          Logout
        </button>
      </div>
    </aside>
  );
}
