import React from 'react';
import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { conflictFieldLabel, conflictGuidance, formatConflictValue, hasConflictField } from '../concurrency';

export function ConflictNotice({ conflict, refreshError = '', bulk = false }) {
  if (!conflict) {
    return null;
  }

  return (
    <Alert severity="warning" variant="outlined" role="status">
      <AlertTitle>Newer ticket changes found</AlertTitle>
      {conflictGuidance(conflict, { bulk })}
      {refreshError ? <Typography component="span" display="block" variant="body2"> Latest details could not be loaded: {refreshError}</Typography> : null}
    </Alert>
  );
}

export function ConflictFieldNote({ conflict, fields, id }) {
  if (!hasConflictField(conflict, fields)) {
    return null;
  }

  return <span id={id} className="conflict-field-note">Changed on the server. Review this value.</span>;
}

export function ConflictFieldResolution({ field, localValue, latestValue, descriptionId, onKeep, onUseLatest, keepLabel = 'Keep my draft', useLatestLabel = 'Use latest value' }) {
  const fieldLabel = conflictFieldLabel(field);
  return (
    <Paper component="section" variant="outlined" className="conflict-resolution" aria-labelledby={`${descriptionId}-title`}>
      <Typography id={`${descriptionId}-title`} variant="subtitle2" sx={{ fontWeight: 800 }}>{fieldLabel}</Typography>
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ my: 1 }}>
        <div className="conflict-value">
          <Typography variant="caption" color="text.secondary">My draft</Typography>
          <Typography component="div" variant="body2">{formatConflictValue(localValue)}</Typography>
        </div>
        <div className="conflict-value">
          <Typography variant="caption" color="text.secondary">Latest server value</Typography>
          <Typography component="div" variant="body2">{formatConflictValue(latestValue)}</Typography>
        </div>
      </Stack>
      <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
        <Button type="button" size="small" variant="outlined" aria-label={`${keepLabel} for ${fieldLabel}`} onClick={onKeep}>{keepLabel}</Button>
        {onUseLatest ? <Button type="button" size="small" variant="outlined" aria-label={`${useLatestLabel} for ${fieldLabel}`} onClick={onUseLatest}>{useLatestLabel}</Button> : null}
      </Stack>
    </Paper>
  );
}

export function ConflictSnapshotReview({ conflict, localTicket, latestTicket, excludeFields = [], onReview }) {
  const excluded = new Set(excludeFields);
  const fields = (conflict?.changedFields || []).filter((field) => !excluded.has(field));
  return fields.map((field) => (
    <ConflictFieldResolution
      key={field}
      field={field}
      localValue={localTicket?.[field]}
      latestValue={latestTicket?.[field]}
      descriptionId={`conflict-${localTicket?.id || 'ticket'}-${field}`}
      onKeep={() => onReview?.([field])}
      onUseLatest={null}
      keepLabel="Mark reviewed"
    />
  ));
}
