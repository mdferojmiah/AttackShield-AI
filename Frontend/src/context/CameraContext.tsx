/**
 * CameraContext
 *
 * Stores camera list across navigations so we don't re-fetch from the
 * backend every time the user switches pages.
 * 
 * Streaming lifecycle is NOT managed here — LiveFeedPage starts streams
 * on mount and stops them on unmount.
 */

import React, {
  createContext,
  useContext,
  useState,
  useCallback,
  useEffect,
} from 'react';
import { CamerasAPI } from '@/services/api';
import { UserStorage } from '@/services/storage';
import { useAuth } from './AuthContext';

export interface CameraData {
  id: string;
  camera_name: string;
  stream_url: string;
  location: string;
}

interface CameraContextValue {
  cameras: CameraData[];
  setCameras: React.Dispatch<React.SetStateAction<CameraData[]>>;
  userName: string;
  loading: boolean;
  loaded: boolean;
  loadCameras: () => Promise<void>;
}

const CameraContext = createContext<CameraContextValue | null>(null);

export function CameraProvider({ children }: { children: React.ReactNode }) {
  const { isAuthenticated } = useAuth();
  const [cameras, setCameras] = useState<CameraData[]>([]);
  const [userName, setUserName] = useState('');
  const [loading, setLoading] = useState(false);
  const [loaded, setLoaded] = useState(false);

  const loadCameras = useCallback(async () => {
    setLoading(true);
    try {
      const userData = UserStorage.getUser();
      setUserName(userData?.name || '');

      const cameraList: CameraData[] = [];

      // Primary camera from user profile
      const primaryUrl =
        (userData?.camera as any)?.rtsp_url || userData?.rtspUrl;
      if (primaryUrl) {
        cameraList.push({
          id: 'primary',
          camera_name:
            userData?.camera?.camera_name ||
            userData?.cctvName ||
            'CCTV Camera',
          stream_url: primaryUrl,
          location:
            userData?.camera?.location ||
            userData?.location ||
            'Main Entrance',
        });
      }

      // Extra cameras from backend
      const res = await CamerasAPI.list();
      if (res.success && res.data) {
        (res.data as any[]).forEach((cam) => {
          if (cam.id === 'primary') return;
          cameraList.push({
            id: cam.id,
            camera_name: cam.name,
            stream_url: cam.rtspUrl,
            location: cam.location,
          });
        });
      }

      setCameras(cameraList);
      setLoaded(true);
    } catch (err) {
      console.error('Error loading cameras:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  // Load cameras when authenticated
  useEffect(() => {
    if (isAuthenticated && !loaded) {
      loadCameras();
    }
    // Reset on logout
    if (!isAuthenticated && loaded) {
      setCameras([]);
      setUserName('');
      setLoaded(false);
    }
  }, [isAuthenticated, loaded, loadCameras]);

  return (
    <CameraContext.Provider
      value={{
        cameras,
        setCameras,
        userName,
        loading,
        loaded,
        loadCameras,
      }}
    >
      {children}
    </CameraContext.Provider>
  );
}

export function useCameras() {
  const ctx = useContext(CameraContext);
  if (!ctx) throw new Error('useCameras must be inside CameraProvider');
  return ctx;
}
