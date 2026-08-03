import React, { useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import TextField from '@mui/material/TextField';

const REASONS = ['Ticket was made by mistake', 'Ticket is inaccurate', 'Could not reproduce', 'Other'];

export default function CancelTicketDialog({ ticket, submitting, error, onCancel, onClose }) {
  const [reason, setReason] = useState(REASONS[0]);
  const [otherReason, setOtherReason] = useState('');
  const selectedReason = reason === 'Other' ? otherReason.trim() : reason;

  return (
    <Dialog open={Boolean(ticket)} onClose={onClose} aria-labelledby="cancel-ticket-title" maxWidth="xs" fullWidth>
      <DialogTitle id="cancel-ticket-title">Cancel Ticket Without A Solution?</DialogTitle>
      <DialogContent>
        <Alert severity="warning" sx={{ mb: 2 }}>This ticket has no solution report. It will be archived as cancelled, not closed.</Alert>
        <FormControl component="fieldset" fullWidth>
          <RadioGroup value={reason} onChange={(event) => setReason(event.target.value)} aria-label="Cancellation reason">
            {REASONS.map((option) => <FormControlLabel key={option} value={option} control={<Radio />} label={option} />)}
          </RadioGroup>
        </FormControl>
        {reason === 'Other' ? <TextField label="Reason" value={otherReason} onChange={(event) => setOtherReason(event.target.value)} multiline minRows={3} fullWidth sx={{ mt: 1 }} /> : null}
        {error ? <Alert severity="error" role="alert" sx={{ mt: 2 }}>{error}</Alert> : null}
      </DialogContent>
      <DialogActions>
        <Button type="button" variant="outlined" onClick={onClose}>Keep Ticket Open</Button>
        <Button type="button" color="warning" onClick={() => onCancel(selectedReason)} disabled={submitting || !selectedReason}>{submitting ? 'Cancelling...' : 'Archive As Cancelled'}</Button>
      </DialogActions>
    </Dialog>
  );
}
