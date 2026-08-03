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
import { ConflictFieldNote, ConflictFieldResolution, ConflictNotice, ConflictSnapshotReview } from './ConcurrencyConflict';
import { conflictFields, hasConflictField } from '../concurrency';

export default function AllocatePanel({ ticket, latestTicket = null, users, selectedAssignee, loading = false, submitting = false, error = '', conflict = null, conflictRefreshError = '', onConflictReview, onAssigneeChange, onAllocate, onClose }) {
  const [reconfirmed, setReconfirmed] = useState(false);

  useEffect(() => setReconfirmed(false), [conflict?.currentVersion]);

  if (!ticket) {
    return null;
  }

  const latestStatus = latestTicket?.status || conflict?.currentStatus;
  const actionStillValid = !conflict || ['todo', 'open', 'reopened'].includes(latestStatus);
  const unresolved = conflictFields(conflict);

  return (
    <Dialog open onClose={onClose} aria-label="Allocate ticket" maxWidth="sm">
      <DialogTitle sx={{ pr: 7 }}>
        Allocate Ticket
        <IconButton type="button" className="report-close" aria-label="Close allocate panel" onClick={onClose} sx={{ position: 'absolute', top: 12, right: 12 }}>
          <CloseIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent dividers>
        <Typography className="report-ticket-title" color="text.secondary" sx={{ mb: 2 }}>{ticket.issueTitle}</Typography>
        {loading ? <CircularProgress aria-label="loading assignees" size={24} /> : null}
        <ConflictNotice conflict={conflict} refreshError={conflictRefreshError} />
        {error && !conflict ? <Alert severity="error" role="alert" sx={{ my: 1 }}>{error}</Alert> : null}
        {!loading ? (
          <Stack component="form" id="allocate-form" className="allocate-form" spacing={2} onSubmit={onAllocate}>
            <label htmlFor="allocate-assignee">Allocate To</label>
            <select
              id="allocate-assignee"
              value={selectedAssignee}
              onChange={(event) => { onAssigneeChange(event.target.value); onConflictReview?.(['assigneeUserId']); }}
              disabled={submitting || users.length === 0}
              className={hasConflictField(conflict, 'assigneeUserId') ? 'conflict-field' : undefined}
              aria-describedby={hasConflictField(conflict, 'assigneeUserId') ? 'allocate-assignee-conflict' : undefined}
            >
              <option value="">Select a developer</option>
              {users.map((user) => <option key={user.userId} value={user.userId}>{formatUserIdentity(user)}</option>)}
            </select>
            <ConflictFieldNote conflict={conflict} fields="assigneeUserId" id="allocate-assignee-conflict" />
            {hasConflictField(conflict, 'assigneeUserId') ? (
              <ConflictFieldResolution
                field="assigneeUserId"
                localValue={selectedAssignee}
                latestValue={latestTicket?.assigneeUserId}
                descriptionId="allocate-assignee-resolution"
                onKeep={() => onConflictReview?.(['assigneeUserId'])}
                onUseLatest={() => { onAssigneeChange(latestTicket?.assigneeUserId || ''); onConflictReview?.(['assigneeUserId']); }}
              />
            ) : null}
            <ConflictSnapshotReview conflict={conflict} localTicket={ticket} latestTicket={latestTicket} excludeFields={['assigneeUserId']} onReview={onConflictReview} />
            {conflict ? (
              <Alert severity="warning" variant="outlined" role="status">
                {actionStillValid ? (
                  <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
                    <span>Assignment is still available. Reconfirm assignment against the latest ticket.</span>
                    <Button type="button" size="small" variant={reconfirmed ? 'contained' : 'outlined'} onClick={() => setReconfirmed(true)}>{reconfirmed ? 'Assignment reconfirmed' : 'Reconfirm assignment'}</Button>
                  </Stack>
                ) : 'This ticket is no longer active, so assignment is obsolete.'}
              </Alert>
            ) : null}
          </Stack>
        ) : null}
      </DialogContent>
      <DialogActions>
        <Button type="button" variant="outlined" onClick={onClose}>Cancel</Button>
        <Button type="submit" form="allocate-form" disabled={submitting || !selectedAssignee || unresolved.length > 0 || (Boolean(conflict) && (!reconfirmed || !actionStillValid))}>{submitting ? 'Attributing...' : 'Attribute'}</Button>
      </DialogActions>
    </Dialog>
  );
}
