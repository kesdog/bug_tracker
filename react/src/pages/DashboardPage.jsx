import React from 'react';
import Box from '@mui/material/Box';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import TicketTable from '../components/TicketTable';
import { EmptyState, ErrorAlert, LoadingState, MetricCard, SeverityChip } from '../components/MuiPrimitives';
import { getProjectName } from '../table_utils';

const columns = [
  {
    key: 'issueTitle',
    label: 'Bug',
    sortable: true,
    defaultDirection: 'asc'
  },
  {
    key: 'reporterUserId',
    label: 'Reported By',
    sortable: true,
    defaultDirection: 'asc'
  },
  {
    key: 'assigneeUserId',
    label: 'Assigned To',
    sortable: true,
    defaultDirection: 'asc',
    render: (ticket) => ticket.assigneeUserId || '-'
  },
  {
    key: 'assignedAt',
    label: 'Active Since',
    sortable: true,
    defaultDirection: 'desc'
  },
  {
    key: 'projectName',
    label: 'Project',
    sortable: true,
    defaultDirection: 'asc',
    render: (ticket) => getProjectName(ticket)
  },
  {
    key: 'severity',
    label: 'Severity',
    sortable: true,
    defaultDirection: 'desc',
    render: (ticket) => <SeverityChip value={ticket.severity} />
  }
];

export default function DashboardPage({ tickets, loading, error, summary = null, summaryError = '', allocatedCount = 0, onViewAllocated, onViewTickets }) {
  const urgentCount = summary?.urgentActive ?? 0;
  const unassignedCount = summary?.unassignedActive ?? 0;
  const rowMenuItems = onViewTickets ? [{ key: 'open-view-tickets', label: 'Open in View Tickets', onSelect: onViewTickets }] : [];

  return (
    <Box component="section" className="dashboard">
      <Typography component="h2" variant="h5">Dashboard</Typography>
      <Typography color="text.secondary" sx={{ mb: 2 }}>Track your current scope quickly before diving into ticket details.</Typography>

       <Box aria-label="Dashboard summary" sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', xl: 'repeat(4, minmax(0, 1fr))' }, gap: 2, my: 2 }}>
         <MetricCard label="Active tickets" value={summary?.activeTotal ?? 0} note="Exact visible total" onClick={onViewTickets} />
         <MetricCard label="Allocated tickets" value={allocatedCount} note="Assigned to you" onClick={onViewAllocated} actionLabel={`View allocated bugs: ${allocatedCount} allocated tickets assigned to you`} />
         <MetricCard label="Urgent tickets" value={urgentCount} note="Urgent severity or P0" tone="error" />
         <MetricCard label="Unassigned" value={unassignedCount} note="Needs triage ownership" tone="warning" />
       </Box>

       <ErrorAlert>{summaryError}</ErrorAlert>
       <ErrorAlert>{error}</ErrorAlert>

      {loading ? <LoadingState label="loading active tickets" /> : null}

      {!loading && !error && tickets.length === 0 ? <EmptyState title="No active tickets yet." description="New reports will appear here as soon as they enter the active queue." /> : null}

      {!loading && tickets.length > 0 ? (
        <Stack spacing={1.5}>
          <Typography variant="h6">Active ticket preview</Typography>
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
