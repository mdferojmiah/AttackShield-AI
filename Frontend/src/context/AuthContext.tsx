/**
 * Authentication Context
 * Provides global authentication state management for the web app
 */

import React, {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  type ReactNode,
} from 'react';
import { AuthAPI } from '@/services/api';
import { UserStorage } from '@/services/storage';
import type { User, LoginFormData } from '@/types';
import { getSocketInstance } from './SocketContext';

interface AuthState {
  user: User | null;
  token: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  error: string | null;
}

interface AuthContextType extends AuthState {
  login: (credentials: LoginFormData) => Promise<boolean>;
  logout: () => void;
  clearError: () => void;
  refreshUser: () => void;
  updateUser: (updates: Partial<User>) => boolean;
  /** alias for isLoading */
  loading: boolean;
}

const initialState: AuthState = {
  user: null,
  token: null,
  isAuthenticated: false,
  isLoading: true,
  error: null,
};

const AuthContext = createContext<AuthContextType | undefined>(undefined);

interface AuthProviderProps {
  children: ReactNode;
}

export function AuthProvider({ children }: AuthProviderProps) {
  const [state, setState] = useState<AuthState>(initialState);

  // Initialise from localStorage on mount
  useEffect(() => {
    const user = UserStorage.getUser();
    const token = UserStorage.getToken();

    if (user && token) {
      setState({
        user,
        token,
        isAuthenticated: true,
        isLoading: false,
        error: null,
      });
    } else {
      setState({ ...initialState, isLoading: false });
    }
  }, []);

  const login = useCallback(
    async (credentials: LoginFormData): Promise<boolean> => {
      setState((prev) => ({ ...prev, isLoading: true, error: null }));

      try {
        const result = await AuthAPI.login(credentials);

        if (result.success && result.data) {
          const { user, token } = result.data;
          UserStorage.setUser(user);
          UserStorage.setToken(token);

          setState({
            user,
            token,
            isAuthenticated: true,
            isLoading: false,
            error: null,
          });
          return true;
        } else {
          setState((prev) => ({
            ...prev,
            isLoading: false,
            error: result.error || 'Login failed',
          }));
          return false;
        }
      } catch (error: unknown) {
        const msg =
          error instanceof Error ? error.message : 'Network error';
        setState((prev) => ({
          ...prev,
          isLoading: false,
          error: msg,
        }));
        return false;
      }
    },
    [],
  );

  const logout = useCallback(() => {
    const socket = getSocketInstance();
    if (socket?.connected && state.user) {
      socket.emit('stop-detection', { user: state.user.name });
    }
    UserStorage.logout();
    setState({ ...initialState, isLoading: false });
  }, [state.user]);

  const clearError = useCallback(() => {
    setState((prev) => ({ ...prev, error: null }));
  }, []);

  const refreshUser = useCallback(() => {
    const user = UserStorage.getUser();
    if (user) {
      setState((prev) => ({ ...prev, user }));
    }
  }, []);

  const updateUser = useCallback(
    (updates: Partial<User>): boolean => {
      if (!state.user) return false;
      const updatedUser = { ...state.user, ...updates };
      const success = UserStorage.setUser(updatedUser);
      if (success) {
        setState((prev) => ({ ...prev, user: updatedUser }));
      }
      return success;
    },
    [state.user],
  );

  const value: AuthContextType = {
    ...state,
    loading: state.isLoading,
    login,
    logout,
    clearError,
    refreshUser,
    updateUser,
  };

  return (
    <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
  );
}

export function useAuth(): AuthContextType {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}

export default AuthContext;
