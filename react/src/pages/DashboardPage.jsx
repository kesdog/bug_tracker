import React from 'react';
import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import TicketTable from '../components/TicketTable';
import { EmptyState, ErrorAlert, LoadingState, MetricCard, SeverityChip } from '../components/MuiPrimitives';
import { getProjectName } from '../table_utils';
import { useI18n } from '../i18n';

function buildColumns(t) {
  return [
  {
    key: 'issueTitle',
    label: t('common.bug', 'Bug'),
    sortable: true,
    defaultDirection: 'asc'
  },
  {
    key: 'reporterUserId',
    label: t('common.reportedBy', 'Reported By'),
    sortable: true,
    defaultDirection: 'asc'
  },
  {
    key: 'assigneeUserId',
    label: t('common.assignedTo', 'Assigned To'),
    sortable: true,
    defaultDirection: 'asc',
    render: (ticket) => ticket.assigneeUserId || '-'
  },
  {
    key: 'assignedAt',
    label: t('common.activeSince', 'Active Since'),
    sortable: true,
    defaultDirection: 'desc'
  },
  {
    key: 'projectName',
    label: t('common.project', 'Project'),
    sortable: true,
    defaultDirection: 'asc',
    render: (ticket) => getProjectName(ticket)
  },
  {
    key: 'severity',
    label: t('common.severity', 'Severity'),
    sortable: true,
    defaultDirection: 'desc',
    render: (ticket) => <SeverityChip value={ticket.severity} />
  }
  ];
}

export default function DashboardPage({ tickets, loading, error, summary = null, summaryError = '', allocatedCount = 0, onViewAllocated, onViewTickets }) {
  const { t } = useI18n();
  const columns = buildColumns(t);
  const urgentCount = summary?.urgentActive ?? 0;
  const unassignedCount = summary?.unassignedActive ?? 0;
  const rowMenuItems = onViewTickets ? [{ key: 'open-view-tickets', label: t('pages.dashboard.openInViewTickets', 'Open in View Tickets'), onSelect: onViewTickets }] : [];

  return (
    <Box component="section" className="dashboard">
      <Typography component="h2" variant="h5">{t('pages.dashboard.title', 'Dashboard')}</Typography>
      <Typography color="text.secondary" sx={{ mb: 2 }}>{t('pages.dashboard.subtitle', 'Track your current scope quickly before diving into ticket details.')}</Typography>

        <Box aria-label={t('pages.dashboard.summaryLabel', 'Dashboard summary')} sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', xl: 'repeat(4, minmax(0, 1fr))' }, gap: 2, my: 2 }}>
          <MetricCard label={t('pages.dashboard.activeTickets', 'Active tickets')} value={summary?.activeTotal ?? 0} note={t('pages.dashboard.exactVisibleTotal', 'Exact visible total')} onClick={onViewTickets} />
          <MetricCard label={t('pages.dashboard.allocatedTickets', 'Allocated tickets')} value={allocatedCount} note={t('pages.dashboard.assignedToYou', 'Assigned to you')} onClick={onViewAllocated} actionLabel={`${t('pages.dashboard.viewAllocatedBugs', 'View allocated bugs')}: ${allocatedCount} ${t('pages.dashboard.allocatedTicketsAssignedToYou', 'allocated tickets assigned to you')}`} />
          <MetricCard label={t('pages.dashboard.urgentTickets', 'Urgent tickets')} value={urgentCount} note={t('pages.dashboard.urgentSeverityOrP0', 'Urgent severity or P0')} tone="error" />
          <MetricCard label={t('pages.dashboard.unassigned', 'Unassigned')} value={unassignedCount} note={t('pages.dashboard.needsTriageOwnership', 'Needs triage ownership')} tone="warning" />
       </Box>

       <ErrorAlert>{summaryError}</ErrorAlert>
       <ErrorAlert>{error}</ErrorAlert>

      {loading ? <LoadingState label={t('pages.dashboard.loadingActiveTickets', 'loading active tickets')} /> : null}

      {!loading && !error && tickets.length === 0 ? <EmptyState title={t('pages.dashboard.emptyTitle', 'No active tickets yet.')} description={t('pages.dashboard.emptyDescription', 'New reports will appear here as soon as they enter the active queue.')} /> : null}

      {!loading && tickets.length > 0 ? (
        <Stack spacing={1.5}>
          <Typography variant="h6">{t('pages.dashboard.activeTicketPreview', 'Active ticket preview')}</Typography>
          <TicketTable
            tickets={tickets}
            columns={columns}
            defaultSort={{ key: 'assignedAt', direction: 'desc' }}
            rowMenuItems={rowMenuItems}
            wrapClassName="dashboard-ticket-scroll"
          />
        </Stack>
      ) : null}
    </Box>
  );
}
