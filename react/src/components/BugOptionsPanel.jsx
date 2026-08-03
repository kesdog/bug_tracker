import React from 'react';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import CloseIcon from '@mui/icons-material/Close';

export default function BugOptionsPanel({ ticket, onEditBugReport, onModifyReport, modifyReportLabel = 'Modify Solution Steps', onEditMetadata, onCloseBug, onClose }) {
  if (!ticket) {
    return null;
  }

  return (
    <Dialog open onClose={onClose} aria-label="Bug options" maxWidth="sm">
      <DialogTitle sx={{ pr: 7 }}>
        Bug Options
        <IconButton type="button" className="report-close" aria-label="Close options panel" onClick={onClose} sx={{ position: 'absolute', top: 12, right: 12 }}>
          <CloseIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent dividers>
        <Typography className="report-ticket-title" color="text.secondary" sx={{ mb: 2 }}>{ticket.issueTitle}</Typography>
        <Stack className="action-panel-buttons" spacing={1.25}>
          <Button type="button" onClick={onEditBugReport}>
            Edit Bug Report
          </Button>
          <Button type="button" onClick={onModifyReport}>
            {modifyReportLabel}
          </Button>
          {onEditMetadata ? (
            <Button type="button" onClick={onEditMetadata}>
              Edit Metadata
            </Button>
          ) : null}
          <Button type="button" color="error" onClick={onCloseBug}>
            Close Bug
          </Button>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button type="button" variant="outlined" onClick={onClose}>Cancel</Button>
      </DialogActions>
    </Dialog>
  );
}
