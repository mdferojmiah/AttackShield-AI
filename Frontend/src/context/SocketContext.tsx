/**
 * Socket Context
 * SignalR connection management for real-time events.
 * Replaces the original Socket.IO client. Event names are preserved verbatim
 * (detection-overlay, weapon-detected, notification-created, alert-created,
 * detection-started) so consumer components keep using connection.on(...).
 */

import React, {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import {
  HubConnectionBuilder,
  HubConnectionState,
  type HubConnection,
} from '@microsoft/signalr';
import { API_CONFIG } from '@/config';
import { UserStorage } from '@/services/storage';

interface SocketContextType {
  socket: HubConnection | null;
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

// Module-level reference to the latest connection instance
let latestSocket: HubConnection | null = null;

export function getSocketInstance(): HubConnection | null {
  return latestSocket;
}

export function SocketProvider({ children }: SocketProviderProps) {
  const socketRef = useRef<HubConnection | null>(null);
  const [socket, setSocket] = useState<HubConnection | null>(null);

  const sendDetectionRequest = (payload: {
    stream_url: string;
    user: string;
    location: string;
    camera_name?: string;
    camera_id?: string;
  }) => {
    if (socketRef.current?.state === HubConnectionState.Connected) {
      // Hub method StartDetection expects camelCase keys (StartDetectionPayload).
      socketRef.current
        .invoke('StartDetection', {
          streamUrl: payload.stream_url,
          location: payload.location,
          user: payload.user,
          cameraName: payload.camera_name,
          cameraId: payload.camera_id,
        })
        .catch((error) =>
          console.error('[Socket] StartDetection invoke failed:', error),
        );
      console.log('[Socket] Sent detection request:', payload);
    } else {
      console.warn('[Socket] Not connected. Cannot send detection request.');
    }
  };

  useEffect(() => {
    console.log('[Socket] Connecting to:', API_CONFIG.BASE_URL);

    const connection = new HubConnectionBuilder()
      .withUrl(`${API_CONFIG.BASE_URL}/socket`, {
        // The .NET JwtBearer OnMessageReceived handler reads the token from the
        // access_token query for /socket paths. Harmless if the hub is anonymous.
        accessTokenFactory: () => UserStorage.getToken() ?? '',
      })
      .withAutomaticReconnect([0, 1500, 3000, 5000, 10000])
      .build();

    socketRef.current = connection;
    setSocket(connection);
    latestSocket = connection;

    connection.onreconnected((connectionId) => {
      console.log('[Socket] Reconnected', connectionId);
    });

    connection.onclose((error) => {
      console.log('[Socket] Disconnected:', error?.message ?? 'closed');
    });

    connection
      .start()
      .then(() => console.log('[Socket] Connected', connection.connectionId))
      .catch((error) =>
        console.error('[Socket] Connection error:', error?.message ?? error),
      );

    return () => {
      console.log('[Socket] Disconnecting…');
      connection.stop();
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
