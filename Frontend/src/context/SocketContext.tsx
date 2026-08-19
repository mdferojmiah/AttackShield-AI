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
  /**
   * Increments on every successful (re)connect. Consumers that registered
   * server-side state (e.g. an active detection session) should treat a change
   * here as "the backend forgot about me" and re-register.
   */
  connectionEpoch: number;
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
  const [connectionEpoch, setConnectionEpoch] = useState(0);

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
    let retryTimer: ReturnType<typeof setTimeout> | undefined;
    let attempt = 0;
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

    // An automatic reconnect produces a brand-new hub connection server-side,
    // so any detection session started on the old one is gone. Bump the epoch
    // so consumers re-issue their requests.
    connection.onreconnected(() => {
      if (disposed) return;
      console.log('[SignalR] Reconnected');
      setConnectionEpoch((value) => value + 1);
    });

    // withAutomaticReconnect() only retries connections that succeeded at
    // least once, so an initial failure (e.g. the frontend booted before the
    // backend was listening) would otherwise leave `socket` null forever and
    // silently disable every detection request. Retry the first connect
    // ourselves with capped exponential backoff.
    const attemptStart = () => {
      if (disposed) return;
      void connection.start()
        .then(() => {
          if (disposed) return;
          attempt = 0;
          setSocket(s);
          setConnectionEpoch((value) => value + 1);
          console.log('[SignalR] Connected');
        })
        .catch((error) => {
          if (disposed) return;
          attempt += 1;
          const delay = Math.min(1000 * 2 ** (attempt - 1), 15000);
          console.warn(
            `[SignalR] Connection attempt ${attempt} failed; retrying in ${delay} ms`,
            error,
          );
          retryTimer = setTimeout(attemptStart, delay);
        });
    };
    attemptStart();

    return () => {
      disposed = true;
      if (retryTimer) clearTimeout(retryTimer);
      console.log('[SignalR] Disconnecting');
      s.disconnect();
      socketRef.current = null;
      setSocket(null);
      latestSocket = null;
    };
  }, [isAuthenticated]);

  return (
    <SocketContext.Provider value={{ socket, connectionEpoch, sendDetectionRequest, stopDetectionRequest }}>
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
