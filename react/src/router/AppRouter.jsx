import React, { useEffect, useState } from 'react';
import Dashboard from '../layouts/Dashboard';
import CredentialRecoveryPage from '../pages/CredentialRecoveryPage';
import LoginPage from '../pages/LoginPage';
import SetupPasswordPage from '../pages/SetupPasswordPage';
import { getAppHeaderMeta } from '../appViewConfig';
import { useI18n } from '../i18n';
import { useAuth } from '../providers/AuthProvider';
import { useSession } from '../providers/SessionProvider';
import { readAppUrlState, writeAppUrlState } from '../url_state';

const AGENT_UNSAFE_PAGES = ['project-management', 'user-management', 'audit-logs'];
const ADMIN_ONLY_PAGES = ['user-management', 'audit-logs'];

function canAccessPage(page, user) {
  if (!user) return false;
  if (AGENT_UNSAFE_PAGES.includes(page) && user.userType === 'agent') return false;
  return !ADMIN_ONLY_PAGES.includes(page) || user.role === 'admin';
}

export default function AppRouter() {
  const { session } = useSession();
  const { view, showView } = useAuth();
  const { t } = useI18n();
  const [urlState, setUrlState] = useState(readAppUrlState);
  const [currentPage, setCurrentPage] = useState(() => readAppUrlState().view);
  const [navigationContext, setNavigationContext] = useState({});
  const isSetupRoute = window.location.pathname === '/setup-password';
  const headerMeta = getAppHeaderMeta({ session, isSetupRoute, loginView: view, currentPage, t });

  useEffect(() => {
    document.title = `${headerMeta.title} | Bug Tracker`;
  }, [headerMeta.title]);

  function applyUrlState(next) {
    const safeState = canAccessPage(next.view, session?.user) ? next : { ...next, view: 'dashboard', ticket: '' };
    setNavigationContext({});
    setUrlState(safeState);
    setCurrentPage(safeState.view);
  }

  useEffect(() => {
    const restore = () => applyUrlState(readAppUrlState());
    window.addEventListener('popstate', restore);
    return () => window.removeEventListener('popstate', restore);
  }, [session?.user?.role, session?.user?.userType]);

  useEffect(() => {
    if (session?.user && !canAccessPage(currentPage, session.user)) {
      const next = { ...urlState, view: 'dashboard', ticket: '' };
      setCurrentPage('dashboard');
      setUrlState(next);
      writeAppUrlState(next, { replace: true });
    }
  }, [currentPage, session?.user]);

  function navigate(page, context = {}) {
    setNavigationContext(context);
    setCurrentPage(page);
    const next = { view: page, search: '', quick: 'all', filters: context.ticketFilters || {}, ticket: '' };
    setUrlState(next);
    writeAppUrlState(next);
  }

  if (session) return <Dashboard currentPage={currentPage} urlState={urlState} navigationContext={navigationContext} onNavigate={navigate} />;
  if (isSetupRoute) return <SetupPasswordPage />;
  if (view === 'recovery') return <CredentialRecoveryPage />;
  return <LoginPage />;
}
