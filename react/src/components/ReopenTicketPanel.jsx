import React, { useEffect, useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import CloseIcon from '@mui/icons-material/Close';
import { ConflictNotice, ConflictSnapshotReview } from './ConcurrencyConflict';
import { conflictFields } from '../concurrency';

export default function ReopenTicketPanel({ ticket, latestTicket = null, submitting = false, error = '', conflict = null, conflictRefreshError = '', onConflictReview, onSubmit, onClose }) {
  const [reason, setReason] = useState('');
  const [validationError, setValidationError] = useState('');
  const [reconfirmed, setReconfirmed] = useState(false);

  useEffect(() => setReconfirmed(false), [conflict?.currentVersion]);

  if (!ticket) {
    return null;
  }
  const latestStatus = latestTicket?.status || conflict?.currentStatus;
  const actionStillValid = !conflict || latestStatus === 'closed';
  const unresolved = conflictFields(conflict);

  function submitForm(event) {
    event.preventDefault();
    const normalizedReason = reason.trim();
    if (!normalizedReason) {
      setValidationError('Reopen reason is required.');
      return;
    }

    setValidationError('');
    onSubmit(normalizedReason);
  }

  return (
    <Dialog open onClose={onClose} aria-label="Reopen ticket" maxWidth="sm">
      <DialogTitle sx={{ pr: 7 }}>
        Reopen Ticket
        <IconButton type="button" className="report-close" aria-label="Close reopen panel" onClick={onClose} sx={{ position: 'absolute', top: 12, right: 12 }}>
          <CloseIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent dividers>
        <Typography className="report-ticket-title" color="text.secondary" sx={{ mb: 2 }}>{ticket.issueTitle}</Typography>
        <Stack component="form" id="reopen-ticket-form" className="metadata-form" spacing={2} onSubmit={submitForm}>
          <TextField
            id="reopen-reason"
            label="Reason"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Why should this ticket return to active work?"
            rows={5}
            multiline
            fullWidth
          />
          <ConflictNotice conflict={conflict} refreshError={conflictRefreshError} />
          <ConflictSnapshotReview conflict={conflict} localTicket={ticket} latestTicket={latestTicket} onReview={onConflictReview} />
          {conflict ? (
            <Alert severity="warning" variant="outlined" role="status">
              {actionStillValid ? (
                <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
                  <span>The ticket is still closed. Reconfirm that it should be reopened using the latest version.</span>
                  <Button type="button" size="small" variant={reconfirmed ? 'contained' : 'outlined'} onClick={() => setReconfirmed(true)}>{reconfirmed ? 'Reopen reconfirmed' : 'Reconfirm reopen'}</Button>
                </Stack>
              ) : 'This ticket is already active, so reopening it is obsolete. Cancel and review the latest ticket.'}
            </Alert>
          ) : null}
          {validationError || (error && !conflict) ? <Alert severity="error" role="alert">{validationError || error}</Alert> : null}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button type="button" variant="outlined" onClick={onClose}>Cancel</Button>
        <Button type="submit" form="reopen-ticket-form" disabled={submitting || unresolved.length > 0 || (Boolean(conflict) && (!reconfirmed || !actionStillValid))}>{submitting ? 'Reopening...' : 'Reopen Ticket'}</Button>
      </DialogActions>
    </Dialog>
  );
}
