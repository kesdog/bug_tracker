import React, { useMemo } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { alpha } from '@mui/material/styles';
import { DataGrid } from '@mui/x-data-grid';
import { formatTicketDate } from '../table_utils';
import { formatLastActive, RequestStatusChip, UserPresenceChip } from '../user_management_utils';

function gridSx(theme, centered = false) {
  return {
    borderRadius: 3,
    bgcolor: 'background.paper',
    '--DataGrid-overlayHeight': '220px',
    '& .MuiDataGrid-row': { cursor: 'pointer' },
    '& .MuiDataGrid-row.request-row-odd': {
      bgcolor: alpha(theme.palette.primary.main, 0.045),
      ...theme.applyStyles('dark', { bgcolor: alpha(theme.palette.primary.main, 0.10) })
    },
    '& .MuiDataGrid-row:hover, & .MuiDataGrid-row.request-row-odd:hover': {
      bgcolor: alpha(theme.palette.primary.main, 0.10),
      ...theme.applyStyles('dark', { bgcolor: alpha(theme.palette.primary.main, 0.18) })
    },
    '& .MuiDataGrid-columnHeaderTitle': { fontWeight: 900, letterSpacing: '0.03em', textTransform: 'uppercase' },
    ...(centered ? {
      '& .MuiDataGrid-cell': { justifyContent: 'center', textAlign: 'center' },
      '& .MuiDataGrid-columnHeader, & .MuiDataGrid-columnHeaderTitleContainer': { justifyContent: 'center' }
    } : {})
  };
}

export function AccessRequestCard({ requestType, email, emailConfirm, saving, onRequestTypeChange, onEmailChange, onEmailConfirmChange, onSubmit }) {
  return (
    <Card sx={{ my: 2 }}>
      <CardContent>
        <Typography component="h3" variant="h6" sx={{ fontWeight: 900, mb: 2 }}>Create Access Request</Typography>
        <Box component="form" onSubmit={onSubmit} sx={{ display: 'grid', gap: 2, gridTemplateColumns: { xs: '1fr', md: '0.75fr 1fr 1fr auto' }, alignItems: 'end' }}>
          <TextField id="requestType" select label="Type" value={requestType} onChange={(event) => onRequestTypeChange(event.target.value)}>
            <MenuItem value="human">Human</MenuItem>
            <MenuItem value="ai_agent">AI agent</MenuItem>
          </TextField>
          <TextField id="requestEmail" type="email" label="Email" value={email} onChange={(event) => onEmailChange(event.target.value)} placeholder="newuser@example.com" />
          <TextField id="requestEmailConfirm" type="email" label="Confirm Email" value={emailConfirm} onChange={(event) => onEmailConfirmChange(event.target.value)} placeholder="newuser@example.com" />
          <Button type="submit" disabled={saving} sx={{ minHeight: 54 }}>{saving ? 'Saving...' : 'Create Request'}</Button>
        </Box>
      </CardContent>
    </Card>
  );
}

export function UsersGridCard({ loading, userRows, onOpenMenu }) {
  const columns = useMemo(() => [
    { field: 'username', headerName: 'Username', flex: 1, minWidth: 170 },
    { field: 'email', headerName: 'Email', flex: 1.35, minWidth: 220 },
    { field: 'role', headerName: 'Role', flex: 0.7, minWidth: 120, renderCell: (params) => <Chip size="small" label={params.row.role} variant="outlined" sx={{ fontWeight: 800 }} /> },
    { field: 'userType', headerName: 'Type', flex: 0.7, minWidth: 120, valueGetter: (value) => value || 'human' },
    { field: 'presenceStatus', headerName: 'Status', flex: 1, minWidth: 180, renderCell: (params) => <UserPresenceChip user={params.row} /> },
    { field: 'lastSeenAt', headerName: 'Last active', flex: 1, minWidth: 150, valueGetter: (value) => value || '', renderCell: (params) => formatLastActive(params.row.lastSeenAt) }
  ], []);

  return (
    <Card sx={{ mt: 2 }}>
      <CardContent>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ mb: 1.5, justifyContent: 'space-between', alignItems: { xs: 'stretch', sm: 'center' } }}>
          <Typography component="h3" variant="h6" sx={{ fontWeight: 900 }}>Workspace Users</Typography>
          <Chip label={loading ? 'Loading users' : `${userRows.length} visible`} variant="outlined" />
        </Stack>
        <Typography className="table-action-hint" color="text.secondary">Click or tap a user for options.</Typography>
        <Box sx={{ height: Math.min(620, Math.max(360, 145 + userRows.length * 58)), width: '100%' }}>
          <DataGrid
            rows={userRows}
            columns={columns}
            loading={loading}
            disableRowSelectionOnClick
            hideFooter
            aria-label="Users"
            onRowClick={(params, event) => onOpenMenu(params.row, event.clientX, event.clientY)}
            onCellKeyDown={(params, event) => {
              if (event.key !== 'Enter' && event.key !== ' ') {
                return;
              }

              event.preventDefault();
              event.defaultMuiPrevented = true;
              const rect = event.currentTarget.getBoundingClientRect();
              onOpenMenu(params.row, rect.left + rect.width / 2, rect.top + rect.height / 2);
            }}
            getRowClassName={(params) => (params.indexRelativeToCurrentPage % 2 === 0 ? 'request-row-even' : 'request-row-odd')}
            sx={(theme) => gridSx(theme, true)}
          />
        </Box>
      </CardContent>
    </Card>
  );
}

export function RequestsGridCard({ loading, gridRows, activeTab, humanCount, aiCount, onActiveTabChange, onOpenMenu }) {
  const columns = useMemo(() => [
    { field: 'email', headerName: 'Email', flex: 1.35, minWidth: 220 },
    { field: 'username', headerName: 'Username', flex: 1, minWidth: 170, valueGetter: (value) => value || '-' },
    { field: 'purpose', headerName: 'Purpose', flex: 0.9, minWidth: 155, valueGetter: (value) => value === 'credential_recovery' ? 'Credential recovery' : 'Account access' },
    { field: 'status', headerName: 'Status', flex: 0.7, minWidth: 130, renderCell: (params) => <RequestStatusChip status={params.row.status} /> },
    { field: 'createdAt', headerName: 'Created', flex: 1, minWidth: 180, valueGetter: (value) => value || '', renderCell: (params) => formatTicketDate(params.row.createdAt) }
  ], []);

  return (
    <Card>
      <CardContent>
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ mb: 1.5, justifyContent: 'space-between', alignItems: { xs: 'stretch', sm: 'center' } }}>
          <Tabs value={activeTab} onChange={(event, value) => onActiveTabChange(value)} aria-label="Request type tabs">
            <Tab value="human" label={`Human Requests (${humanCount})`} />
            <Tab value="ai_agent" label={`AI Agent Requests (${aiCount})`} />
          </Tabs>
          <Chip label={loading ? 'Loading requests' : `${gridRows.length} visible`} variant="outlined" />
        </Stack>
        <Typography className="table-action-hint" color="text.secondary">Click or tap a request for options.</Typography>

        <Box
          sx={{ height: Math.min(620, Math.max(360, 145 + gridRows.length * 58)), width: '100%' }}
        >
          <DataGrid
            rows={gridRows}
            columns={columns}
            loading={loading}
            disableRowSelectionOnClick
            hideFooter
            aria-label={activeTab === 'human' ? 'Human Requests' : 'AI Agent Requests'}
            onRowClick={(params, event) => {
              if (event.target?.closest?.('button, a, input, select, textarea')) {
                return;
              }

              onOpenMenu(params.row, event.clientX, event.clientY);
            }}
            onCellKeyDown={(params, event) => {
              if (event.key !== 'Enter' && event.key !== ' ') {
                return;
              }

              event.preventDefault();
              event.defaultMuiPrevented = true;
              const rect = event.currentTarget.getBoundingClientRect();
              onOpenMenu(params.row, rect.left + rect.width / 2, rect.top + rect.height / 2);
            }}
            getRowClassName={(params) => (params.indexRelativeToCurrentPage % 2 === 0 ? 'request-row-even' : 'request-row-odd')}
            sx={gridSx}
          />
        </Box>
      </CardContent>
    </Card>
  );
}
