/**
 * Socket Context
 * Socket.IO connection management for real-time events
 */

import React, {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import io, { type Socket } from 'socket.io-client';
import { API_CONFIG } from '@/config';

interface SocketContextType {
  socket: Socket | null;
  sendDetectionRequest: (payload: {
    stream_url: string;
    user: string;
    location: string;
    camera_name?: string;
    camera_id?: string;
  }) => void;
}

interface SocketProviderProps {
  children: ReactNode;
}

const SocketContext = createContext<SocketContextType | undefined>(undefined);

// Module-level reference to the latest socket instance
let latestSocket: Socket | null = null;

export function getSocketInstance(): Socket | null {
  return latestSocket;
}

export function SocketProvider({ children }: SocketProviderProps) {
  const socketRef = useRef<Socket | null>(null);
  const [socket, setSocket] = useState<Socket | null>(null);

  const sendDetectionRequest = (payload: {
    stream_url: string;
    user: string;
    location: string;
    camera_name?: string;
    camera_id?: string;
  }) => {
    if (socketRef.current?.connected) {
      socketRef.current.emit('start-detection', payload);
      console.log('[Socket] Sent detection request:', payload);
    } else {
      console.warn('[Socket] Not connected. Cannot send detection request.');
    }
  };

  useEffect(() => {
    console.log('[Socket] Connecting to:', API_CONFIG.BASE_URL);

    const s = io(API_CONFIG.BASE_URL, {
      transports: ['websocket', 'polling'],
      timeout: 30000,
      forceNew: true,
      reconnection: true,
      reconnectionAttempts: 10,
      reconnectionDelay: 1500,
    });

    socketRef.current = s;
    setSocket(s);
    latestSocket = s;

    s.on('connect', () => {
      console.log('[Socket] Connected', s.id);
    });

    s.on('disconnect', (reason) => {
      console.log('[Socket] Disconnected:', reason);
    });

    s.on('connect_error', (error) => {
      console.error('[Socket] Connection error:', error.message);
    });

    s.on('reconnect', (attemptNumber: number) => {
      console.log('[Socket] Reconnected after', attemptNumber, 'attempts');
    });

    return () => {
      console.log('[Socket] Disconnecting…');
      s.disconnect();
      socketRef.current = null;
      setSocket(null);
      latestSocket = null;
    };
  }, []);

  return (
    <SocketContext.Provider value={{ socket, sendDetectionRequest }}>
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
