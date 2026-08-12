import React from 'react';
import { AuthProvider } from './providers/AuthProvider';
import { SessionProvider } from './providers/SessionProvider';
import AppRouter from './router/AppRouter';

export default function App() {
  return (
    <SessionProvider>
      <AuthProvider>
        <AppRouter />
      </AuthProvider>
    </SessionProvider>
  );
}
