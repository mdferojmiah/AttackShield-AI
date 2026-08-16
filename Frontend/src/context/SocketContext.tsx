/**
 * Realtime context
 * SignalR connection management for the ASP.NET detection hub.
 */

import React, {
  useCallback,
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { API_CONFIG } from '@/config';
import { UserStorage } from '@/services/storage';
import { useAuth } from './AuthContext';

type Handler = (...args: any[]) => void;

interface RealtimeClient {
  connected: boolean;
  on: (event: string, handler: Handler) => void;
  off: (event: string, handler: Handler) => void;
  emit: (event: string, payload?: unknown) => Promise<void>;
  disconnect: () => void;
}

function createRealtimeClient(connection: HubConnection): RealtimeClient {
  const handlers = new Map<string, Set<Handler>>();
  return {
    get connected() {
      return connection.state === 'Connected';
    },
    on(event, handler) {
      if (!handlers.has(event)) handlers.set(event, new Set());
      handlers.get(event)!.add(handler);
      connection.on(event, handler);
    },
    off(event, handler) {
      handlers.get(event)?.delete(handler);
      connection.off(event, handler);
    },
    emit(event, payload) {
      const method = event === 'start-detection' ? 'StartDetection' : 'StopDetection';
      return connection.invoke(method, payload);
    },
    disconnect() {
      void connection.stop();
    },
  };
}

interface SocketContextType {
  socket: RealtimeClient | null;
  sendDetectionRequest: (payload: {
    stream_url: string;
    user: string;
    location: string;
    camera_name?: string;
    camera_id?: string;
  }) => void;
  stopDetectionRequest: (cameraId?: string) => void;
}

interface SocketProviderProps {
  children: ReactNode;
}

const SocketContext = createContext<SocketContextType | undefined>(undefined);

// Module-level reference to the latest socket instance
let latestSocket: RealtimeClient | null = null;

export function getSocketInstance(): RealtimeClient | null {
  return latestSocket;
}

export function SocketProvider({ children }: SocketProviderProps) {
  const { isAuthenticated } = useAuth();
  const socketRef = useRef<RealtimeClient | null>(null);
  const [socket, setSocket] = useState<RealtimeClient | null>(null);

  const sendDetectionRequest = useCallback((payload: {
    stream_url: string;
    user: string;
    location: string;
    camera_name?: string;
    camera_id?: string;
  }) => {
    if (socketRef.current?.connected) {
      void socketRef.current.emit('start-detection', {
        streamUrl: payload.stream_url,
        user: payload.user,
        location: payload.location,
        cameraName: payload.camera_name,
        cameraId: payload.camera_id,
      }).catch((error) => console.error('[Socket] Detection start failed:', error));
      console.log('[Socket] Sent detection request:', payload);
    } else {
      console.warn('[Socket] Not connected. Cannot send detection request.');
    }
  }, []);

  const stopDetectionRequest = useCallback((cameraId?: string) => {
    if (!socketRef.current?.connected) return;
    void socketRef.current.emit('stop-detection', cameraId ? { cameraId } : undefined)
      .catch((error) => console.error('[Socket] Detection stop failed:', error));
  }, []);

  useEffect(() => {
    if (!isAuthenticated) {
      latestSocket = null;
      socketRef.current = null;
      setSocket(null);
      return;
    }

    let disposed = false;
    const connection = new HubConnectionBuilder()
      .withUrl(`${API_CONFIG.BASE_URL}/hubs/detection`, {
        accessTokenFactory: () => UserStorage.getToken() || '',
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    const s = createRealtimeClient(connection);
    socketRef.current = s;
    latestSocket = s;
    void connection.start()
      .then(() => {
        if (!disposed) setSocket(s);
      })
      .catch((error) => {
        if (!disposed) console.error('[SignalR] Connection error:', error);
      });

    return () => {
      disposed = true;
      console.log('[SignalR] Disconnecting');
      s.disconnect();
      socketRef.current = null;
      setSocket(null);
      latestSocket = null;
    };
  }, [isAuthenticated]);

  return (
    <SocketContext.Provider value={{ socket, sendDetectionRequest, stopDetectionRequest }}>
      {children}
    </SocketContext.Provider>
  );
}

export function useSocket() {
  const context = useContext(SocketContext);
  if (context === undefined) {
    throw new Error('useSocket must be used within a SocketProvider');
  }
  return context;
}
