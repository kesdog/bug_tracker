import React, { useEffect, useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import CloseIcon from '@mui/icons-material/Close';
import { formatUserIdentity } from '../user_identity';
import { ConflictFieldNote, ConflictFieldResolution } from './ConcurrencyConflict';
import { conflictFields, conflictGuidance, hasConflictField } from '../concurrency';

export default function BulkAllocatePanel({ tickets = [], ticketIds, users, selectedAssignee, loading = false, submitting = false, error = '', conflicts = [], retrying = false, retryReady = true, onConflictReview, onAssigneeChange, onAllocate, onClose }) {
  const [reconfirmed, setReconfirmed] = useState(false);
  const safeIds = Array.isArray(ticketIds) ? ticketIds.filter(Boolean) : [];
  const aggregateConflict = conflicts.length > 0 ? { changedFields: [...new Set(conflicts.flatMap((item) => item.changedFields || []))] } : null;
  const unresolvedCount = conflicts.reduce((total, item) => total + conflictFields(item).length, 0);

  useEffect(() => setReconfirmed(false), [conflicts.map((item) => item.currentVersion).join(',')]);

  return (
    <Dialog open onClose={onClose} aria-label="Bulk assign visible tickets" maxWidth="sm">
      <DialogTitle sx={{ pr: 7 }}>
        Bulk Assign Visible Tickets
        <IconButton type="button" className="report-close" aria-label="Close bulk assign panel" onClick={onClose} sx={{ position: 'absolute', top: 12, right: 12 }}>
          <CloseIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent dividers>
        <Typography className="report-ticket-title" color="text.secondary">This will assign {safeIds.length} pending ticket{safeIds.length === 1 ? '' : 's'}.</Typography>
        <Typography className="bulk-id-preview" variant="body2" color="text.secondary" sx={{ my: 1.5 }}>IDs: {safeIds.slice(0, 8).join(', ')}{safeIds.length > 8 ? `, +${safeIds.length - 8} more` : ''}</Typography>
        {loading ? <CircularProgress aria-label="loading assignees" size={24} /> : null}
        {conflicts.length > 0 ? (
          <Alert severity="warning" variant="outlined" sx={{ my: 1 }}>
            {conflictGuidance(aggregateConflict, { bulk: true })}
            <ul className="bulk-conflict-list">
              {conflicts.map((item) => <li key={item.ticketId}>{item.ticketId}: version {item.currentVersion ?? 'updated'} ({(item.changedFields || []).join(', ') || 'ticket changed'})</li>)}
            </ul>
          </Alert>
        ) : null}
        {error ? <Alert severity="error" sx={{ my: 1 }}>{error}</Alert> : null}
        {!loading ? (
          <Stack component="form" id="bulk-allocate-form" className="allocate-form" spacing={2} onSubmit={onAllocate}>
            <label htmlFor="bulk-allocate-assignee">Assign Visible Tickets To</label>
            <select
              id="bulk-allocate-assignee"
              value={selectedAssignee}
              onChange={(event) => onAssigneeChange(event.target.value)}
              disabled={submitting || users.length === 0 || safeIds.length === 0}
              className={hasConflictField(aggregateConflict, 'assigneeUserId') ? 'conflict-field' : undefined}
              aria-describedby={hasConflictField(aggregateConflict, 'assigneeUserId') ? 'bulk-assignee-conflict' : undefined}
            >
              <option value="">Select a developer</option>
              {users.map((user) => <option key={user.userId} value={user.userId}>{formatUserIdentity(user)}</option>)}
            </select>
            <ConflictFieldNote conflict={aggregateConflict} fields="assigneeUserId" id="bulk-assignee-conflict" />
            {conflicts.flatMap((item) => conflictFields(item).map((field) => {
              const localTicket = tickets.find((ticket) => ticket.id === item.ticketId);
              const localValue = field === 'assigneeUserId' ? selectedAssignee : localTicket?.[field];
              return (
                <ConflictFieldResolution
                  key={`${item.ticketId}-${field}`}
                  field={`${item.ticketId}: ${field}`}
                  localValue={localValue}
                  latestValue={item.latestTicket?.[field]}
                  descriptionId={`bulk-${item.ticketId}-${field}`}
                  onKeep={() => onConflictReview?.([field], item.ticketId)}
                  onUseLatest={null}
                  keepLabel="Mark reviewed"
                />
              );
            }))}
            {conflicts.length > 0 ? (
              <Alert severity="warning" variant="outlined" role="status">
                <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
                  <span>Bulk assignment is never retried automatically. Reconfirm after reviewing every affected ticket.</span>
                  <Button type="button" size="small" variant={reconfirmed ? 'contained' : 'outlined'} onClick={() => setReconfirmed(true)}>{reconfirmed ? 'Bulk assignment reconfirmed' : 'Reconfirm bulk assignment'}</Button>
                </Stack>
              </Alert>
            ) : null}
          </Stack>
        ) : null}
      </DialogContent>
      <DialogActions>
        <Button type="button" variant="outlined" onClick={onClose}>Cancel</Button>
        <Button type="submit" form="bulk-allocate-form" disabled={submitting || !selectedAssignee || safeIds.length === 0 || unresolvedCount > 0 || (retrying && !retryReady) || (conflicts.length > 0 && !reconfirmed)}>{submitting ? 'Assigning...' : retrying ? `Retry ${safeIds.length} Failed Tickets` : `Assign ${safeIds.length} Visible Tickets`}</Button>
      </DialogActions>
    </Dialog>
  );
}
