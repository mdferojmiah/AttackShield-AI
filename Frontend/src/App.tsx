import React from 'react';
import { Toaster } from 'react-hot-toast';
import { AuthProvider, SocketProvider, ThemeProvider, CameraProvider } from '@/context';
import { ErrorBoundary } from '@/components';
import AppRoutes from '@/routes';

export default function App() {
  return (
    <ErrorBoundary>
      <ThemeProvider>
        <AuthProvider>
          <SocketProvider>
            <CameraProvider>
              <AppRoutes />
            </CameraProvider>
            <Toaster
              position="top-right"
              toastOptions={{
                duration: 4000,
                style: {
                  background: '#1E293B',
                  color: '#F8FAFC',
                  border: '1px solid #334155',
                },
                success: {
                  iconTheme: { primary: '#10B981', secondary: '#F8FAFC' },
                },
                error: {
                  iconTheme: { primary: '#EF4444', secondary: '#F8FAFC' },
                },
              }}
            />
          </SocketProvider>
        </AuthProvider>
      </ThemeProvider>
    </ErrorBoundary>
  );
}
