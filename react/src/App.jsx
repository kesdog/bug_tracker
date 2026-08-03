import React, { lazy, Suspense, useEffect, useRef, useState } from 'react';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import { fetchMe, login, logout, requestAccess, requestCredentialRecovery, setupPassword } from './api/auth';
import { clearBugCache, fetchBugSummary, fetchDashboardBugs } from './api/bugs';
import { SESSION_UNAUTHORIZED_EVENT } from './api/client';
import NavBar from './components/NavBar';
import { PageHeader } from './components/MuiPrimitives';
import { AuthLayout, CredentialRecoveryForm, DemoLoginPanel, LoginForm, RequestAccessForm, SetupPasswordForm } from './components/AuthViews';
import { readDemoConfig } from './demo_config';
const AddBugPage = lazy(() => import('./pages/AddBugPage'));
const AuditLogsPage = lazy(() => import('./pages/AuditLogsPage'));
const AllocatedPage = lazy(() => import('./pages/AllocatedPage'));
const ArchivedPage = lazy(() => import('./pages/ArchivedPage'));
const DashboardPage = lazy(() => import('./pages/DashboardPage'));
const ProjectManagementPage = lazy(() => import('./pages/ProjectManagementPage'));
const SubmittedReportsPage = lazy(() => import('./pages/SubmittedReportsPage'));
const UserManagementPage = lazy(() => import('./pages/UserManagementPage'));
const ViewTicketsPage = lazy(() => import('./pages/ViewTicketsPage'));
import { readAppUrlState, writeAppUrlState } from './url_state';
import { getAppHeaderMeta } from './appViewConfig';
import {
  clearStoredSession,
  createSessionManager,
  initializeSessionActivity,
  isStoredSessionInactive,
  SESSION_INACTIVITY_TIMEOUT_MS,
  SESSION_TOKEN_KEY
} from './session_manager';

export default function App() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [demoConfig] = useState(readDemoConfig);
  const [authLoading, setAuthLoading] = useState(false);
  const [dashboardLoading, setDashboardLoading] = useState(false);
  const [error, setError] = useState('');
  const [session, setSession] = useState(null);
  const [tickets, setTickets] = useState([]);
  const [dashboardError, setDashboardError] = useState('');
  const [urlState, setUrlState] = useState(readAppUrlState);
  const [currentPage, setCurrentPage] = useState(() => readAppUrlState().view);
  const [navigationContext, setNavigationContext] = useState({});
  const [allocatedCount, setAllocatedCount] = useState(0);
  const [dashboardSummary, setDashboardSummary] = useState(null);
  const [summaryError, setSummaryError] = useState('');
  const isSetupRoute = typeof window !== 'undefined' && window.location.pathname === '/setup-password';
  const searchParams = typeof window !== 'undefined' ? new URLSearchParams(window.location.search) : new URLSearchParams();
  const setupTokenFromLink = searchParams.get('token') || '';
  const setupEmailFromLink = (searchParams.get('email') || '').toLowerCase();
  const [setupEmail, setSetupEmail] = useState('');
  const [setupEmailConfirm, setSetupEmailConfirm] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [newPasswordConfirm, setNewPasswordConfirm] = useState('');
  const [setupSuccessMessage, setSetupSuccessMessage] = useState('');
  const [loginView, setLoginView] = useState('login');
  const [requestType, setRequestType] = useState('human');
  const [requestEmail, setRequestEmail] = useState('');
  const [requestEmailConfirm, setRequestEmailConfirm] = useState('');
  const [recoveryType, setRecoveryType] = useState('human');
  const [recoveryEmail, setRecoveryEmail] = useState('');
  const [recoveryEmailConfirm, setRecoveryEmailConfirm] = useState('');
  const endingSessionRef = useRef(false);

  function clearSessionState(reason) {
    clearStoredSession();
    clearBugCache();
    setSession(null);
    setTickets([]);
    setAllocatedCount(0);
    setDashboardError('');
    setSummaryError('');
    setCurrentPage('dashboard');
    setNavigationContext({});
    setEmail('');
    setPassword('');
    const inactivityMinutes = Math.round(SESSION_INACTIVITY_TIMEOUT_MS / 60_000);
    setError(
      reason === 'inactive'
        ? `You were signed out after ${inactivityMinutes} minutes of inactivity.`
        : reason === 'unauthorized'
          ? 'Your session expired or was revoked. Please sign in again.'
          : ''
    );
  }

  function endSession(reason, tokenOverride) {
    if (endingSessionRef.current) {
      return;
    }

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

  useEffect(() => {
    if (!isSetupRoute) {
      return;
    }

    setSetupEmail(setupEmailFromLink);
    setSetupEmailConfirm(setupEmailFromLink);
  }, [isSetupRoute, setupEmailFromLink]);

  async function loadDashboard(token) {
    setDashboardLoading(true);
    setDashboardError('');

    setSummaryError('');
    try {
      const [previewResult, summaryResult] = await Promise.allSettled([fetchDashboardBugs(token, 10), fetchBugSummary(token)]);
      if (previewResult.status === 'fulfilled') setTickets(Array.isArray(previewResult.value) ? previewResult.value : previewResult.value?.items || []);
      else { setTickets([]); setDashboardError(previewResult.reason?.message || 'Unable to load active ticket preview.'); }
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

  useEffect(() => {
    const handleUnauthorized = () => endSession('unauthorized');
    window.addEventListener(SESSION_UNAUTHORIZED_EVENT, handleUnauthorized);
    return () => window.removeEventListener(SESSION_UNAUTHORIZED_EVENT, handleUnauthorized);
  }, [session?.token]);

  useEffect(() => {
    const restore = () => {
      const next = readAppUrlState();
      const unsafe = ['project-management', 'user-management', 'audit-logs'];
      if (session?.user?.userType === 'agent' && unsafe.includes(next.view)) next.view = 'dashboard';
      if (next.view === 'user-management' || next.view === 'audit-logs') {
        if (session?.user?.role !== 'admin') next.view = 'dashboard';
      }
      setNavigationContext({});
      setUrlState(next);
      setCurrentPage(next.view);
    };
    window.addEventListener('popstate', restore);
    return () => window.removeEventListener('popstate', restore);
  }, [session?.user?.role, session?.user?.userType]);

  useEffect(() => {
    if (!session?.user) return;
    const adminOnly = currentPage === 'user-management' || currentPage === 'audit-logs';
    const agentUnsafe = ['project-management', 'user-management', 'audit-logs'].includes(currentPage);
    if ((adminOnly && session.user.role !== 'admin') || (agentUnsafe && session.user.userType === 'agent')) {
      setCurrentPage('dashboard');
      setUrlState((current) => ({ ...current, view: 'dashboard', ticket: '' }));
      writeAppUrlState({ view: 'dashboard', ticket: '' }, { replace: true });
    }
  }, [currentPage, session?.user]);

  useEffect(() => {
    if (!session?.token) {
      return undefined;
    }

    const manager = createSessionManager({
      onEnd: ({ reason, token }) => endSession(reason, token)
    });
    manager.start();
    return () => manager.stop();
  }, [session?.token]);

  useEffect(() => {
    const token = localStorage.getItem(SESSION_TOKEN_KEY);
    if (!token) {
      return;
    }

    if (isStoredSessionInactive()) {
      endSession('inactive', token);
      return;
    }

    setAuthLoading(true);
    fetchMe(token)
      .then(async (user) => {
        setSession({ token, user });
        await loadDashboard(token);
      })
      .catch(() => {
        clearStoredSession();
      })
      .finally(() => setAuthLoading(false));
  }, []);

  async function handleSubmit(event) {
    event.preventDefault();
    setError('');

    if (!email.trim() || !password.trim()) {
      setError('Email and password are required.');
      return;
    }

    setAuthLoading(true);
    try {
      const result = await login(email.trim(), password);
      localStorage.setItem(SESSION_TOKEN_KEY, result.accessToken);
      initializeSessionActivity();
      setSession({ token: result.accessToken, user: result.user });
      setPassword('');
      const requested = readAppUrlState();
      const adminOnly = requested.view === 'user-management' || requested.view === 'audit-logs';
      const agentUnsafe = ['project-management', 'user-management', 'audit-logs'].includes(requested.view);
      const requestedView = (adminOnly && result.user.role !== 'admin') || (agentUnsafe && result.user.userType === 'agent')
        ? 'dashboard'
        : requested.view;
      setUrlState({ ...requested, view: requestedView });
      setCurrentPage(requestedView);
      await loadDashboard(result.accessToken);
    } catch (err) {
      setError(err.message || 'Login failed.');
    } finally {
      setAuthLoading(false);
    }
  }

  async function handleSetupSubmit(event) {
    event.preventDefault();
    setError('');
    setSetupSuccessMessage('');

    const emailValue = setupEmail.trim().toLowerCase();
    const confirmEmailValue = setupEmailConfirm.trim().toLowerCase();

    if (!emailValue || !confirmEmailValue || !newPassword || !newPasswordConfirm) {
      setError('All setup fields are required.');
      return;
    }

    if (!setupTokenFromLink) {
      setError('Setup token is missing from link.');
      return;
    }

    if (emailValue !== confirmEmailValue) {
      setError('Email and confirmation email must match.');
      return;
    }

    if (newPassword !== newPasswordConfirm) {
      setError('New password and confirmation must match.');
      return;
    }

    if (!/[0-9]/.test(newPassword) || !/[^A-Za-z0-9]/.test(newPassword) || newPassword.length < 6) {
      setError('Password must be at least 6 characters with one number and one special character.');
      return;
    }

    setAuthLoading(true);
    try {
      await setupPassword(emailValue, setupTokenFromLink, newPassword);
      setSetupSuccessMessage('Password set successfully. You can now sign in.');
      if (typeof window !== 'undefined') {
        window.history.replaceState({}, '', '/');
      }
      setEmail(emailValue);
      setSetupEmail('');
      setSetupEmailConfirm('');
      setNewPassword('');
      setNewPasswordConfirm('');
    } catch (err) {
      setError(err.message || 'Unable to complete user setup.');
    } finally {
      setAuthLoading(false);
    }
  }

  async function handleRequestAccessSubmit(event) {
    event.preventDefault();
    setError('');
    setSetupSuccessMessage('');

    const emailValue = requestEmail.trim().toLowerCase();
    const emailConfirmValue = requestEmailConfirm.trim().toLowerCase();
    if (!emailValue || !emailConfirmValue) {
      setError('Email and confirm email are required.');
      return;
    }

    if (emailValue !== emailConfirmValue) {
      setError('Email and confirm email must match.');
      return;
    }

    setAuthLoading(true);
    try {
      await requestAccess(emailValue, requestType);
      setSetupSuccessMessage(demoConfig
        ? 'Access request submitted for demo review. No email will be sent.'
        : 'Access request submitted. Admin will review your request.');
      setRequestEmail('');
      setRequestEmailConfirm('');
      setLoginView('login');
    } catch (err) {
      setError(err.message || 'Unable to submit access request.');
    } finally {
      setAuthLoading(false);
    }
  }

  async function handleCredentialRecoverySubmit(event) {
    event.preventDefault();
    setError('');
    setSetupSuccessMessage('');
    const emailValue = recoveryEmail.trim().toLowerCase();
    const confirmation = recoveryEmailConfirm.trim().toLowerCase();
    if (!emailValue || !confirmation) {
      setError('Email and confirm email are required.');
      return;
    }
    if (emailValue !== confirmation) {
      setError('Email and confirm email must match.');
      return;
    }

    setAuthLoading(true);
    try {
      const result = await requestCredentialRecovery(emailValue, recoveryType);
      setSetupSuccessMessage(result.message || 'If the account exists, an administrator can review the request.');
      setRecoveryEmail('');
      setRecoveryEmailConfirm('');
      setLoginView('login');
    } catch (err) {
      setError(err.message || 'Unable to submit credential recovery request.');
    } finally {
      setAuthLoading(false);
    }
  }

  function handleLogout() {
    endSession('manual');
  }

  function navigate(page, context = {}) {
    setNavigationContext(context);
    setCurrentPage(page);
    const next = { view: page, search: '', quick: 'all', filters: context.ticketFilters || {}, ticket: '' };
    setUrlState(next);
    writeAppUrlState(next);
  }

  const headerMeta = getAppHeaderMeta({ session, isSetupRoute, loginView, currentPage });

  useEffect(() => {
    document.title = `${headerMeta.title} | Bug Tracker`;
  }, [headerMeta.title]);

  if (session) {
    return (
      <Box sx={{ display: 'flex', minHeight: '100vh' }} data-testid="session-card">
          <NavBar
            currentPage={currentPage}
            onNavigate={(page) => navigate(page)}
          userRole={session.user.role}
          token={session.token}
          user={session.user}
          onLogout={handleLogout}
        />
        <Box
          component="main"
          sx={{
            flexGrow: 1,
            minWidth: 0,
            px: { xs: 2, sm: 3, lg: 5 },
            pt: { xs: 10, md: 11 },
            pb: 5
          }}
        >
          <PageHeader
            title={headerMeta.title}
            description={headerMeta.description}
            action={currentPage === 'dashboard' ? <Chip label={session.user.username || session.user.userId} color="primary" variant="outlined" /> : null}
            eyebrow="Bug operations"
          />

          <Suspense fallback={<Box role="status" sx={{ py: 6, textAlign: 'center' }}>Loading page...</Box>}>
            {currentPage === 'dashboard' ? (
              <DashboardPage
                tickets={tickets}
                loading={dashboardLoading}
                error={dashboardError}
                allocatedCount={allocatedCount}
                summary={dashboardSummary}
                summaryError={summaryError}
                onViewAllocated={() => navigate('allocated')}
                onViewTickets={() => navigate('tickets')}
              />
            ) : currentPage === 'tickets' ? (
               <ViewTicketsPage key={`tickets-${JSON.stringify(urlState)}`} token={session.token} userRole={session.user.role} userType={session.user.userType} currentUserId={session.user.userId} initialFilters={navigationContext.ticketFilters || urlState.filters} initialSearch={urlState.search} initialQuickFilter={urlState.quick} initialTicketId={urlState.ticket} />
            ) : currentPage === 'allocated' ? (
               <AllocatedPage key={`allocated-${JSON.stringify(urlState)}`} token={session.token} userRole={session.user.role} userType={session.user.userType} currentUserId={session.user.userId} initialFilters={urlState.filters} initialSearch={urlState.search} initialQuickFilter={urlState.quick} initialTicketId={urlState.ticket} />
            ) : currentPage === 'submitted' && ((session.user.userType !== 'agent' && (session.user.role === 'dev' || session.user.role === 'senior')) || navigationContext.submittedUserId) ? (
              <SubmittedReportsPage token={session.token} currentUserId={navigationContext.submittedUserId || session.user.userId} />
            ) : currentPage === 'archived' ? (
               <ArchivedPage key={`archived-${JSON.stringify(urlState)}`} token={session.token} userRole={session.user.role} userType={session.user.userType} currentUserId={session.user.userId} initialFilters={navigationContext.ticketFilters || urlState.filters} initialSearch={urlState.search} initialQuickFilter={urlState.quick} initialTicketId={urlState.ticket} />
            ) : currentPage === 'add-bug' ? (
              <AddBugPage token={session.token} userRole={session.user.role} userType={session.user.userType} onCreated={() => loadDashboard(session.token)} />
            ) : currentPage === 'project-management' && session.user.userType !== 'agent' && (session.user.role === 'senior' || session.user.role === 'admin') ? (
              <ProjectManagementPage token={session.token} userRole={session.user.role} userType={session.user.userType} />
            ) : currentPage === 'user-management' && session.user.role === 'admin' ? (
              <UserManagementPage
                token={session.token}
                currentUserId={session.user.userId}
                onViewUserLogs={(user) => navigate('audit-logs', { auditFilters: { search: user.userId } })}
                onViewUserTickets={(user, status) => navigate(status === 'closed' ? 'archived' : 'tickets', { ticketFilters: { assigneeUserId: user.userId } })}
                onViewUserSubmitted={(user) => navigate('submitted', { submittedUserId: user.userId })}
              />
            ) : currentPage === 'audit-logs' && session.user.role === 'admin' ? (
              <AuditLogsPage token={session.token} initialFilters={navigationContext.auditFilters || undefined} />
            ) : (
               <DashboardPage tickets={tickets} loading={dashboardLoading} error={dashboardError} summary={dashboardSummary} summaryError={summaryError} token={session.token} allocatedCount={allocatedCount} onViewAllocated={() => navigate('allocated')} onViewTickets={() => navigate('tickets')} />
            )}
          </Suspense>
        </Box>
      </Box>
    );
  }

  return (
    <AuthLayout title={headerMeta.title} description={headerMeta.description} error={error} successMessage={setupSuccessMessage} loading={authLoading}>
      {!isSetupRoute && loginView === 'login' ? (
        <>
          <LoginForm email={email} password={password} loading={authLoading} onEmailChange={setEmail} onPasswordChange={setPassword} onSubmit={handleSubmit} onRequestAccess={() => { setLoginView('request'); setError(''); setSetupSuccessMessage(''); }} onRecoverCredentials={() => { setLoginView('recovery'); setError(''); setSetupSuccessMessage(''); }} />
          <DemoLoginPanel config={demoConfig} onSelect={(account) => { setEmail(account.email); setPassword(account.password); setError(''); }} />
        </>
      ) : !isSetupRoute && loginView === 'request' ? (
        <RequestAccessForm requestType={requestType} email={requestEmail} confirmEmail={requestEmailConfirm} loading={authLoading} isDemo={Boolean(demoConfig)} onTypeChange={setRequestType} onEmailChange={setRequestEmail} onConfirmEmailChange={setRequestEmailConfirm} onSubmit={handleRequestAccessSubmit} onBack={() => setLoginView('login')} />
      ) : !isSetupRoute && loginView === 'recovery' ? (
        <CredentialRecoveryForm requestType={recoveryType} email={recoveryEmail} confirmEmail={recoveryEmailConfirm} loading={authLoading} isDemo={Boolean(demoConfig)} onTypeChange={setRecoveryType} onEmailChange={setRecoveryEmail} onConfirmEmailChange={setRecoveryEmailConfirm} onSubmit={handleCredentialRecoverySubmit} onBack={() => setLoginView('login')} />
      ) : (
        <SetupPasswordForm email={setupEmail} confirmEmail={setupEmailConfirm} password={newPassword} confirmPassword={newPasswordConfirm} loading={authLoading} onEmailChange={setSetupEmail} onConfirmEmailChange={setSetupEmailConfirm} onPasswordChange={setNewPassword} onConfirmPasswordChange={setNewPasswordConfirm} onSubmit={handleSetupSubmit} />
      )}
    </AuthLayout>
  );
}
