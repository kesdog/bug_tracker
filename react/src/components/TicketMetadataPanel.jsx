import React, { useState } from 'react';
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
import { ConflictFieldNote, ConflictFieldResolution, ConflictNotice, ConflictSnapshotReview } from './ConcurrencyConflict';
import { conflictFields, hasConflictField } from '../concurrency';

const SEVERITIES = ['low', 'mid', 'high', 'urgent'];
const PRIORITIES = ['p0', 'p1', 'p2', 'p3'];
const BUG_TYPES = [
  { value: 'page_not_loading', label: 'Page not loading' },
  { value: 'form_submission', label: 'Form submission' },
  { value: 'crash', label: 'Crash' },
  { value: 'api', label: 'API' },
  { value: 'database', label: 'Database' }
];
const ALLOWED_TAGS = ['front-end', 'back-end', 'regression', 'blocked', 'needs-repro', 'ai-reviewed', 'security', 'performance'];

function tagsToText(tags) {
  return Array.isArray(tags) ? tags.join(', ') : '';
}

export default function TicketMetadataPanel({ ticket, latestTicket = null, submitting = false, error = '', conflict = null, conflictRefreshError = '', onConflictReview, onSubmit, onClose }) {
  const [issueTitle, setIssueTitle] = useState(ticket?.issueTitle || '');
  const [bugType, setBugType] = useState(ticket?.bugType || 'page_not_loading');
  const [projectId, setProjectId] = useState(ticket?.projectId || '');
  const [severity, setSeverity] = useState(ticket?.severity || 'mid');
  const [priority, setPriority] = useState(ticket?.priority || 'p2');
  const [tagsText, setTagsText] = useState(tagsToText(ticket?.tags));
  const [validationError, setValidationError] = useState('');

  if (!ticket) {
    return null;
  }

  function submitForm(event) {
    event.preventDefault();
    const normalizedTitle = issueTitle.trim();
    const normalizedProjectId = projectId.trim();
    const normalizedBugType = bugType.trim() || 'page_not_loading';
    const normalizedTags = tagsText
      .split(',')
      .map((tag) => tag.trim().toLowerCase())
      .filter(Boolean);

    if (!normalizedTitle) {
      setValidationError('Issue title is required.');
      return;
    }

    if (!BUG_TYPES.some((type) => type.value === normalizedBugType) || !SEVERITIES.includes(severity) || !PRIORITIES.includes(priority)) {
      setValidationError('Choose a valid bug type, severity, and priority.');
      return;
    }

    if (severity === 'urgent' && priority !== 'p0' && priority !== 'p1') {
      setValidationError('Urgent tickets must use P0 or P1 priority.');
      return;
    }

    if (normalizedTags.some((tag) => !ALLOWED_TAGS.includes(tag))) {
      setValidationError('Tags must match the allowed ticket tags.');
      return;
    }

    if (normalizedTags.includes('front-end') && normalizedTags.includes('back-end')) {
      setValidationError('Choose front-end or back-end, not both.');
      return;
    }

    setValidationError('');
    onSubmit({
      issueTitle: normalizedTitle,
      bugType: normalizedBugType,
      projectId: normalizedProjectId || null,
      severity,
      priority,
      tags: normalizedTags
    });
  }

  function review(field, setter) {
    return (event) => {
      setter(event.target.value);
      onConflictReview?.([field]);
    };
  }

  function nativeConflictProps(field) {
    const conflicted = hasConflictField(conflict, field);
    return {
      className: conflicted ? 'conflict-field' : undefined,
      'aria-describedby': conflicted ? `metadata-${field}-conflict` : undefined,
    };
  }

  const values = { issueTitle, bugType, projectId, severity, priority, tags: tagsText };
  const setters = { issueTitle: setIssueTitle, bugType: setBugType, projectId: setProjectId, severity: setSeverity, priority: setPriority, tags: (value) => setTagsText(tagsToText(value)) };
  const metadataFields = Object.keys(values);

  function resolution(field) {
    if (!hasConflictField(conflict, field)) return null;
    const latestValue = latestTicket?.[field];
    return (
      <ConflictFieldResolution
        field={field}
        localValue={values[field]}
        latestValue={latestValue}
        descriptionId={`metadata-${field}-resolution`}
        onKeep={() => onConflictReview?.([field])}
        onUseLatest={() => { setters[field](latestValue ?? ''); onConflictReview?.([field]); }}
      />
    );
  }

  return (
    <Dialog open onClose={onClose} aria-label="Edit ticket metadata" maxWidth="md">
      <DialogTitle sx={{ pr: 7 }}>
        Edit Ticket Metadata
        <IconButton type="button" className="report-close" aria-label="Close metadata panel" onClick={onClose} sx={{ position: 'absolute', top: 12, right: 12 }}>
          <CloseIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent dividers>
        <Typography className="report-ticket-title" color="text.secondary" sx={{ mb: 2 }}>{ticket.issueTitle}</Typography>

        <Stack component="form" id="metadata-form" className="metadata-form" spacing={2} onSubmit={submitForm}>
          <TextField id="metadata-title" label="Issue Title" value={issueTitle} onChange={review('issueTitle', setIssueTitle)} className={hasConflictField(conflict, 'issueTitle') ? 'conflict-field' : undefined} helperText={hasConflictField(conflict, 'issueTitle') ? 'Changed on the server. Choose how to resolve it below.' : ''} fullWidth slotProps={{ htmlInput: { maxLength: 200 } }} />
          {resolution('issueTitle')}
          <label htmlFor="metadata-bug-type">Bug Type</label>
          <select id="metadata-bug-type" value={bugType} onChange={review('bugType', setBugType)} {...nativeConflictProps('bugType')}>
            {BUG_TYPES.map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}
          </select>
          <ConflictFieldNote conflict={conflict} fields="bugType" id="metadata-bugType-conflict" />
          {resolution('bugType')}
          <TextField id="metadata-project-id" label="Project ID" value={projectId} onChange={review('projectId', setProjectId)} className={hasConflictField(conflict, 'projectId') ? 'conflict-field' : undefined} helperText={hasConflictField(conflict, 'projectId') ? 'Changed on the server. Choose how to resolve it below.' : ''} placeholder="Project identifier" fullWidth />
          {resolution('projectId')}
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} className="metadata-grid">
            <div className="metadata-field">
              <label htmlFor="metadata-severity">
                Severity
                <select id="metadata-severity" value={severity} onChange={review('severity', setSeverity)} {...nativeConflictProps('severity')}>
                  {SEVERITIES.map((value) => <option key={value} value={value}>{value}</option>)}
                </select>
              </label>
              <ConflictFieldNote conflict={conflict} fields="severity" id="metadata-severity-conflict" />
              {resolution('severity')}
            </div>
            <div className="metadata-field">
              <label htmlFor="metadata-priority">
                Priority
                <select id="metadata-priority" value={priority} onChange={review('priority', setPriority)} {...nativeConflictProps('priority')}>
                  {PRIORITIES.map((value) => <option key={value} value={value}>{value.toUpperCase()}</option>)}
                </select>
              </label>
              <ConflictFieldNote conflict={conflict} fields="priority" id="metadata-priority-conflict" />
              {resolution('priority')}
            </div>
          </Stack>
          <TextField id="metadata-tags" label="Tags" value={tagsText} onChange={review('tags', setTagsText)} className={hasConflictField(conflict, 'tags') ? 'conflict-field' : undefined} helperText={hasConflictField(conflict, 'tags') ? 'Changed on the server. Choose how to resolve it below.' : ''} placeholder="front-end, regression, blocked" fullWidth />
          {resolution('tags')}
          <ConflictSnapshotReview conflict={conflict} localTicket={ticket} latestTicket={latestTicket} excludeFields={metadataFields} onReview={onConflictReview} />
          <ConflictNotice conflict={conflict} refreshError={conflictRefreshError} />
          {validationError || (error && !conflict) ? <Alert severity="error" role="alert">{validationError || error}</Alert> : null}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button type="button" variant="outlined" onClick={onClose}>Cancel</Button>
        <Button type="submit" form="metadata-form" disabled={submitting || conflictFields(conflict).length > 0}>{submitting ? 'Saving...' : 'Save Metadata'}</Button>
      </DialogActions>
    </Dialog>
  );
}
