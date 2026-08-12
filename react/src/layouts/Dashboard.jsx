import React, { lazy, Suspense, useEffect, useState } from 'react';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Alert from '@mui/material/Alert';
import Snackbar from '@mui/material/Snackbar';
import NavBar from '../components/NavBar';
import FirstRunWizard from '../components/FirstRunWizard';
import { PageHeader } from '../components/MuiPrimitives';
import { getAppHeaderMeta } from '../appViewConfig';
import { useI18n } from '../i18n';
import { useSession } from '../providers/SessionProvider';

const AddBugPage = lazy(() => import('../pages/AddBugPage'));
const AuditLogsPage = lazy(() => import('../pages/AuditLogsPage'));
const AllocatedPage = lazy(() => import('../pages/AllocatedPage'));
const ArchivedPage = lazy(() => import('../pages/ArchivedPage'));
const DashboardPage = lazy(() => import('../pages/DashboardPage'));
const ProjectManagementPage = lazy(() => import('../pages/ProjectManagementPage'));
const SubmittedReportsPage = lazy(() => import('../pages/SubmittedReportsPage'));
const UserManagementPage = lazy(() => import('../pages/UserManagementPage'));
const ViewTicketsPage = lazy(() => import('../pages/ViewTicketsPage'));

export default function Dashboard({ currentPage, urlState, navigationContext, onNavigate }) {
  const { t } = useI18n();
  const {
    session,
    tickets,
    dashboardLoading,
    dashboardError,
    allocatedCount,
    dashboardSummary,
    summaryError,
    refreshDashboard,
    endSession
  } = useSession();
  const [setupSuccessMessage, setSetupSuccessMessage] = useState('');
  const headerMeta = getAppHeaderMeta({ session, isSetupRoute: false, loginView: 'login', currentPage, t });

  useEffect(() => {
    document.title = `${headerMeta.title} | Bug Tracker`;
  }, [headerMeta.title]);

  useEffect(() => {
    const message = sessionStorage.getItem('bug-tracker:first-run-complete');
    if (!message) return;
    sessionStorage.removeItem('bug-tracker:first-run-complete');
    setSetupSuccessMessage(message);
  }, []);

  const dashboardPage = (
    <DashboardPage
      tickets={tickets}
      loading={dashboardLoading}
      error={dashboardError}
      summary={dashboardSummary}
      summaryError={summaryError}
      token={session.token}
      allocatedCount={allocatedCount}
      onViewAllocated={() => onNavigate('allocated')}
      onViewTickets={() => onNavigate('tickets')}
    />
  );

  let page = dashboardPage;
  if (currentPage === 'tickets') {
    page = <ViewTicketsPage key={`tickets-${JSON.stringify(urlState)}`} token={session.token} userRole={session.user.role} userType={session.user.userType} currentUserId={session.user.userId} initialFilters={navigationContext.ticketFilters || urlState.filters} initialSearch={urlState.search} initialQuickFilter={urlState.quick} initialTicketId={urlState.ticket} />;
  } else if (currentPage === 'allocated') {
    page = <AllocatedPage key={`allocated-${JSON.stringify(urlState)}`} token={session.token} userRole={session.user.role} userType={session.user.userType} currentUserId={session.user.userId} initialFilters={urlState.filters} initialSearch={urlState.search} initialQuickFilter={urlState.quick} initialTicketId={urlState.ticket} />;
  } else if (currentPage === 'submitted' && ((session.user.userType !== 'agent' && (session.user.role === 'dev' || session.user.role === 'senior')) || navigationContext.submittedUserId)) {
    page = <SubmittedReportsPage token={session.token} currentUserId={navigationContext.submittedUserId || session.user.userId} />;
  } else if (currentPage === 'archived') {
    page = <ArchivedPage key={`archived-${JSON.stringify(urlState)}`} token={session.token} userRole={session.user.role} userType={session.user.userType} currentUserId={session.user.userId} initialFilters={navigationContext.ticketFilters || urlState.filters} initialSearch={urlState.search} initialQuickFilter={urlState.quick} initialTicketId={urlState.ticket} />;
  } else if (currentPage === 'add-bug') {
    page = <AddBugPage token={session.token} userRole={session.user.role} userType={session.user.userType} onCreated={() => refreshDashboard()} />;
  } else if (currentPage === 'project-management' && session.user.userType !== 'agent' && (session.user.role === 'senior' || session.user.role === 'admin')) {
    page = <ProjectManagementPage token={session.token} userRole={session.user.role} userType={session.user.userType} />;
  } else if (currentPage === 'user-management' && session.user.role === 'admin') {
    page = <UserManagementPage token={session.token} currentUserId={session.user.userId} onViewUserLogs={(user) => onNavigate('audit-logs', { auditFilters: { search: user.userId } })} onViewUserTickets={(user, status) => onNavigate(status === 'closed' ? 'archived' : 'tickets', { ticketFilters: { assigneeUserId: user.userId } })} onViewUserSubmitted={(user) => onNavigate('submitted', { submittedUserId: user.userId })} />;
  } else if (currentPage === 'audit-logs' && session.user.role === 'admin') {
    page = <AuditLogsPage token={session.token} initialFilters={navigationContext.auditFilters || undefined} />;
  }

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh' }} data-testid="session-card">
      <NavBar currentPage={currentPage} onNavigate={onNavigate} userRole={session.user.role} token={session.token} user={session.user} onLogout={() => endSession('manual')} />
      <Box component="main" sx={{ flexGrow: 1, minWidth: 0, px: { xs: 2, sm: 3, lg: 5 }, pt: { xs: 10, md: 11 }, pb: 5 }}>
        <PageHeader title={headerMeta.title} description={headerMeta.description} action={currentPage === 'dashboard' ? <Chip label={session.user.username || session.user.userId} color="primary" variant="outlined" /> : null} eyebrow={t('app.bugOperations', 'Bug operations')} />
        <Suspense fallback={<Box role="status" sx={{ py: 6, textAlign: 'center' }}>Loading page...</Box>}>
          {page}
        </Suspense>
      </Box>
      <FirstRunWizard token={session.token} user={session.user} onSessionRevoked={() => endSession('external')} />
      <Snackbar open={Boolean(setupSuccessMessage)} autoHideDuration={8000} onClose={() => setSetupSuccessMessage('')} anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}>
        <Alert severity="success" variant="filled" onClose={() => setSetupSuccessMessage('')}>{setupSuccessMessage}</Alert>
      </Snackbar>
    </Box>
  );
}
