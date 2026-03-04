import React from 'react';
import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from '@/context';
import { LoadingSpinner } from '@/components';

/**
 * Redirects to /login if not authenticated.
 */
export function ProtectedRoute() {
  const { isAuthenticated, loading } = useAuth();

  if (loading) return <LoadingSpinner fullScreen text="Authenticating..." />;
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  return <Outlet />;
}

/**
 * Restricts access to a specific role ('user' or 'authority').
 * Must be nested inside <ProtectedRoute />.
 */
export function RoleRoute({ role }: { role: 'user' | 'authority' }) {
  const { user } = useAuth();

  if (!user) return <Navigate to="/login" replace />;

  const userRole = user.role || 'user';
  if (userRole !== role) {
    return (
      <Navigate
        to={role === 'authority' ? '/dashboard' : '/authority/dashboard'}
        replace
      />
    );
  }

  return <Outlet />;
}

/**
 * Redirects authenticated users away from public pages (login/signup).
 */
export function PublicOnlyRoute() {
  const { isAuthenticated, user, loading } = useAuth();

  if (loading) return <LoadingSpinner fullScreen text="Loading..." />;
  if (isAuthenticated && user) {
    const dest =
      user.role === 'authority' ? '/authority/dashboard' : '/dashboard';
    return <Navigate to={dest} replace />;
  }
  return <Outlet />;
}
