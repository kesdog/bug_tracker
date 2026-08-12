import React, { createContext, useContext, useEffect, useRef, useState } from 'react';
import { fetchMe, logout } from '../api/auth';
import { clearBugCache, fetchBugSummary, fetchDashboardBugs } from '../api/bugs';
import { SESSION_UNAUTHORIZED_EVENT } from '../api/client';
import {
  clearStoredSession,
  createSessionManager,
  isStoredSessionInactive,
  SESSION_INACTIVITY_TIMEOUT_MS,
  SESSION_TOKEN_KEY
} from '../session_manager';

const SessionContext = createContext(null);

export function SessionProvider({ children }) {
  const [session, setSession] = useState(null);
  const [sessionEndReason, setSessionEndReason] = useState('');
  const [isRestoring, setIsRestoring] = useState(true);
  const [dashboardLoading, setDashboardLoading] = useState(false);
  const [tickets, setTickets] = useState([]);
  const [dashboardError, setDashboardError] = useState('');
  const [allocatedCount, setAllocatedCount] = useState(0);
  const [dashboardSummary, setDashboardSummary] = useState(null);
  const [summaryError, setSummaryError] = useState('');
  const endingSessionRef = useRef(false);

  function clearSessionState(reason) {
    clearStoredSession();
    clearBugCache();
    setSession(null);
    setTickets([]);
    setAllocatedCount(0);
    setDashboardSummary(null);
    setDashboardError('');
    setSummaryError('');
    setSessionEndReason(reason);
  }

  function endSession(reason, tokenOverride) {
    if (endingSessionRef.current) return;

    endingSessionRef.current = true;
    const token = tokenOverride || session?.token || localStorage.getItem(SESSION_TOKEN_KEY);
    clearSessionState(reason);

    if (!token || reason === 'external' || reason === 'unauthorized') {
      endingSessionRef.current = false;
      return;
    }

    void logout(token)
      .catch(() => {})
      .finally(() => {
        endingSessionRef.current = false;
      });
  }

  async function refreshDashboard(token = session?.token) {
    if (!token) return;

    setDashboardLoading(true);
    setDashboardError('');
    setSummaryError('');
    try {
      const [previewResult, summaryResult] = await Promise.allSettled([
        fetchDashboardBugs(token, 10),
        fetchBugSummary(token)
      ]);
      if (previewResult.status === 'fulfilled') {
        setTickets(Array.isArray(previewResult.value) ? previewResult.value : previewResult.value?.items || []);
      } else {
        setTickets([]);
        setDashboardError(previewResult.reason?.message || 'Unable to load active ticket preview.');
      }
      if (summaryResult.status === 'fulfilled') {
        setDashboardSummary(summaryResult.value);
        setAllocatedCount(summaryResult.value?.allocatedToMe || 0);
      } else {
        setDashboardSummary(null);
        setAllocatedCount(0);
        setSummaryError(summaryResult.reason?.message || 'Unable to load dashboard summary.');
      }
    } finally {
      setDashboardLoading(false);
    }
  }

  async function startSession(token, user) {
    setSessionEndReason('');
    setSession({ token, user });
    await refreshDashboard(token);
  }

  useEffect(() => {
    const handleUnauthorized = () => endSession('unauthorized');
    window.addEventListener(SESSION_UNAUTHORIZED_EVENT, handleUnauthorized);
    return () => window.removeEventListener(SESSION_UNAUTHORIZED_EVENT, handleUnauthorized);
  }, [session?.token]);

  useEffect(() => {
    if (!session?.token) return undefined;

    const manager = createSessionManager({
      onEnd: ({ reason, token }) => endSession(reason, token)
    });
    manager.start();
    return () => manager.stop();
  }, [session?.token]);

  useEffect(() => {
    const token = localStorage.getItem(SESSION_TOKEN_KEY);
    if (!token) {
      setIsRestoring(false);
      return;
    }

    if (isStoredSessionInactive()) {
      endSession('inactive', token);
      setIsRestoring(false);
      return;
    }

    fetchMe(token)
      .then(async (user) => startSession(token, user))
      .catch(clearStoredSession)
      .finally(() => setIsRestoring(false));
  }, []);

  return (
    <SessionContext.Provider value={{
      session,
      sessionEndReason,
      isRestoring,
      tickets,
      dashboardLoading,
      dashboardError,
      allocatedCount,
      dashboardSummary,
      summaryError,
      startSession,
      refreshDashboard,
      endSession,
      inactivityMinutes: Math.round(SESSION_INACTIVITY_TIMEOUT_MS / 60_000)
    }}>
      {children}
    </SessionContext.Provider>
  );
}

export function useSession() {
  const context = useContext(SessionContext);
  if (!context) throw new Error('useSession must be used within SessionProvider.');
  return context;
}
