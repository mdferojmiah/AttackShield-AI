/**
 * Mobile Header / Bottom Navigation
 * Shows on smaller screens (< lg)
 */

import React, { useState } from 'react';
import { NavLink, useNavigate } from 'react-router-dom';
import {
  HiHome,
  HiVideoCamera,
  HiBell,
  HiCog6Tooth,
  HiBars3,
  HiXMark,
  HiShieldCheck,
  HiClock,
  HiArrowRightOnRectangle,
} from 'react-icons/hi2';
import { useAuth } from '@/context';

export default function MobileNav() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [menuOpen, setMenuOpen] = useState(false);
  const isAuthority = user?.role === 'authority';

  const handleLogout = () => {
    if (window.confirm('Are you sure you want to logout?')) {
      logout();
      navigate('/login');
    }
  };

  const userBottomItems = [
    { path: '/dashboard', label: 'Home', icon: <HiHome size={20} /> },
    { path: '/live-feed', label: 'Live', icon: <HiVideoCamera size={20} /> },
    { path: '/notifications', label: 'Alerts', icon: <HiBell size={20} /> },
    { path: '/settings', label: 'Settings', icon: <HiCog6Tooth size={20} /> },
  ];

  const authorityBottomItems = [
    { path: '/authority/dashboard', label: 'Alerts', icon: <HiShieldCheck size={20} /> },
    { path: '/authority/history', label: 'History', icon: <HiClock size={20} /> },
    { path: '/settings', label: 'Settings', icon: <HiCog6Tooth size={20} /> },
  ];

  const bottomItems = isAuthority ? authorityBottomItems : userBottomItems;

  return (
    <>
      {/* Top bar */}
      <header className="lg:hidden flex items-center justify-between px-4 py-3 bg-light-card dark:bg-dark-surface border-b border-light-border dark:border-dark-border transition-colors duration-200">
        <div className="flex items-center gap-2">
          <HiShieldCheck className="text-primary" size={22} />
          <span className="text-sm font-bold text-slate-800 dark:text-white">WDS</span>
        </div>
        <button
          onClick={() => setMenuOpen(!menuOpen)}
          className="p-2 text-slate-500 dark:text-slate-300 hover:text-slate-800 dark:hover:text-white"
        >
          {menuOpen ? <HiXMark size={22} /> : <HiBars3 size={22} />}
        </button>
      </header>

      {/* Slide-down menu */}
      {menuOpen && (
        <div className="lg:hidden bg-light-card dark:bg-dark-surface border-b border-light-border dark:border-dark-border px-4 py-3 space-y-1">
          <div className="flex items-center gap-3 mb-3 pb-3 border-b border-light-border dark:border-dark-border">
            <div className="w-8 h-8 rounded-full bg-primary flex items-center justify-center text-white text-sm font-semibold">
              {user?.name?.charAt(0).toUpperCase() || 'U'}
            </div>
            <div>
              <p className="text-sm font-medium text-slate-800 dark:text-white">{user?.name}</p>
              <p className="text-xs text-slate-500 dark:text-slate-400">{user?.email}</p>
            </div>
          </div>
          <button
            onClick={handleLogout}
            className="flex items-center gap-2 w-full px-3 py-2 text-sm text-red-400 hover:bg-red-500/10 rounded-lg"
          >
            <HiArrowRightOnRectangle size={18} />
            Logout
          </button>
        </div>
      )}

      {/* Bottom tab bar */}
      <nav className="lg:hidden fixed bottom-0 left-0 right-0 z-50 bg-light-card dark:bg-dark-surface border-t border-light-border dark:border-dark-border flex transition-colors duration-200">
        {bottomItems.map((item) => (
          <NavLink
            key={item.path}
            to={item.path}
            className={({ isActive }) =>
              `flex-1 flex flex-col items-center gap-0.5 py-2.5 text-xs font-medium transition-colors
              ${isActive ? 'text-primary' : 'text-slate-400'}`
            }
          >
            {item.icon}
            {item.label}
          </NavLink>
        ))}
      </nav>
    </>
  );
}
