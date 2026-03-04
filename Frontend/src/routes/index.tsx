import React from 'react';
import { Routes, Route, Navigate } from 'react-router-dom';

import { AppLayout } from '@/layouts';
import { ProtectedRoute, RoleRoute, PublicOnlyRoute } from './guards';

import {
  LoginPage,
  UserSignupPage,
  AuthoritySignupPage,
  DashboardPage,
  LiveFeedPage,
  NotificationsPage,
  NotificationDetailsPage,
  SettingsPage,
  AuthorityDashboardPage,
  AuthorityAlertDetailsPage,
  AuthorityHistoryPage,
} from '@/pages';

export default function AppRoutes() {
  return (
    <Routes>
      {/* ── Public routes (redirect away if logged in) ── */}
      <Route element={<PublicOnlyRoute />}>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/signup" element={<UserSignupPage />} />
        <Route path="/authority/signup" element={<AuthoritySignupPage />} />
      </Route>

      {/* ── Protected routes (require auth) ── */}
      <Route element={<ProtectedRoute />}>
        {/* User layout */}
        <Route element={<RoleRoute role="user" />}>
          <Route element={<AppLayout />}>
            <Route path="/dashboard" element={<DashboardPage />} />
            <Route path="/live-feed" element={<LiveFeedPage />} />
            <Route path="/notifications" element={<NotificationsPage />} />
            <Route
              path="/notifications/:id"
              element={<NotificationDetailsPage />}
            />
            <Route path="/settings" element={<SettingsPage />} />
          </Route>
        </Route>

        {/* Authority layout */}
        <Route element={<RoleRoute role="authority" />}>
          <Route element={<AppLayout />}>
            <Route
              path="/authority/dashboard"
              element={<AuthorityDashboardPage />}
            />
            <Route
              path="/authority/alerts/:id"
              element={<AuthorityAlertDetailsPage />}
            />
            <Route
              path="/authority/history"
              element={<AuthorityHistoryPage />}
            />
            <Route path="/authority/settings" element={<SettingsPage />} />
          </Route>
        </Route>
      </Route>

      {/* ── Fallback ── */}
      <Route path="*" element={<Navigate to="/login" replace />} />
    </Routes>
  );
}
