import React, { useEffect, useState } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import CloseIcon from '@mui/icons-material/Close';
import { MAX_REPORT_TEXT_LENGTH, ReportBuilder } from '../report_builder';
import ReportBuilderEditor from './ReportBuilderEditor';
import { ConflictFieldNote, ConflictFieldResolution, ConflictNotice, ConflictSnapshotReview } from './ConcurrencyConflict';
import { conflictFields as getConflictFields, hasConflictField } from '../concurrency';
import { useI18n } from '../i18n';

export default function BugReportFormPanel({
  ticket,
  title,
  submitLabel,
  notesLabel = 'Solution Steps',
  initialText,
  initialImages,
  submitting,
  error,
  conflict = null,
  latestTicket = null,
  conflictFields = ['postResolutionReport', 'resolutionNotes', 'resolutionReportImages'],
  conflictRefreshError = '',
  actionKind = '',
  onConflictReview,
  onSubmit,
  onClose
}) {
  const { t } = useI18n();
  // Local block state starts from server-provided text/images.
  const [builder, setBuilder] = useState(() => ReportBuilder.fromSerialized(initialText || '', initialImages || []));
  const [builderError, setBuilderError] = useState('');
  const [actionConfirmed, setActionConfirmed] = useState(false);

  // Supports direct replacement and functional updates from child editor.
  function applyBuilder(nextValueOrUpdater) {
    setBuilder((current) => {
      const next = typeof nextValueOrUpdater === 'function' ? nextValueOrUpdater(current) : nextValueOrUpdater;
      const before = current.toPayload();
      const after = next.toPayload();
      const reviewed = [];
      if (before.text !== after.text) reviewed.push(...conflictFields.filter((field) => field !== 'reportImages' && field !== 'resolutionReportImages'));
      if (JSON.stringify(before.images) !== JSON.stringify(after.images)) reviewed.push(...conflictFields.filter((field) => field === 'reportImages' || field === 'resolutionReportImages'));
      if (reviewed.length > 0) onConflictReview?.(reviewed);
      return next;
    });
  }

  // Rehydrate the builder whenever we switch ticket/action context.
  useEffect(() => {
    setBuilder(ReportBuilder.fromSerialized(initialText || '', initialImages || []));
    setBuilderError('');
  }, [ticket?.id, title]);

  useEffect(() => {
    setActionConfirmed(false);
  }, [conflict?.currentVersion, actionKind]);

  // Serializes block editor content and sends it to the parent handler.
  function submit(event) {
    event.preventDefault();
    const payload = builder.toPayload();
    if (!payload.text.trim()) {
      return;
    }

    if (builder.textLength > MAX_REPORT_TEXT_LENGTH) {
      setBuilderError(t('reportForm.textTooLong', 'Report text must be {{count}} characters or less.', { count: MAX_REPORT_TEXT_LENGTH.toLocaleString() }));
      return;
    }

    onSubmit({ text: payload.text.trim(), images: payload.images });
  }

  const currentPayload = builder.toPayload();
  const textConflictFields = conflictFields.filter((field) => field !== 'reportImages' && field !== 'resolutionReportImages');
  const imageConflictFields = conflictFields.filter((field) => field === 'reportImages' || field === 'resolutionReportImages');
  const textConflicted = hasConflictField(conflict, textConflictFields);
  const imageConflicted = hasConflictField(conflict, imageConflictFields);
  const unresolvedFields = getConflictFields(conflict);
  const textConflictNoteId = `report-text-conflict-${ticket?.id || 'ticket'}`;
  const imageConflictNoteId = `report-images-conflict-${ticket?.id || 'ticket'}`;
  const latestText = conflictFields.includes('reportImages')
    ? latestTicket?.description || ''
    : latestTicket?.postResolutionReport || latestTicket?.resolutionNotes || '';
  const latestImages = conflictFields.includes('reportImages') ? latestTicket?.reportImages || [] : latestTicket?.resolutionReportImages || [];
  const isActionConflict = Boolean(actionKind && conflict);
  const actionStillValid = actionKind !== 'close' || ['todo', 'open', 'reopened'].includes(latestTicket?.status || conflict?.currentStatus);

  function useLatestReportField(field) {
    const current = builder.toPayload();
    const isImageField = field === 'reportImages' || field === 'resolutionReportImages';
    setBuilder(ReportBuilder.fromSerialized(isImageField ? current.text : latestText, isImageField ? latestImages : current.images));
    onConflictReview?.(field === 'resolutionNotes' || field === 'postResolutionReport' ? ['resolutionNotes', 'postResolutionReport'] : [field]);
  }

  if (!ticket) {
    return null;
  }

  return (
    <Dialog open onClose={onClose} aria-label={title} maxWidth="md" scroll="paper">
      <DialogTitle sx={{ pr: 7 }}>
        {title}
        <IconButton type="button" className="report-close" aria-label={t('reportForm.close', 'Close action form')} onClick={onClose} sx={{ position: 'absolute', top: 12, right: 12 }}>
          <CloseIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent dividers>
        <Typography className="report-ticket-title" color="text.secondary" sx={{ mb: 2 }}>{ticket.issueTitle}</Typography>

        <Stack component="form" id="bug-report-action-form" className="add-bug-form" spacing={2} onSubmit={submit}>
          <Typography component="label" sx={{ fontWeight: 900 }}>{notesLabel}</Typography>
          <ReportBuilderEditor
            builder={builder}
            label={notesLabel}
            submitting={submitting}
            error={builderError}
            textConflicted={textConflicted}
            imageConflicted={imageConflicted}
            textConflictNoteId={textConflictNoteId}
            imageConflictNoteId={imageConflictNoteId}
            onChange={applyBuilder}
            onError={setBuilderError}
          />
          <ConflictFieldNote conflict={conflict} fields={textConflictFields} id={textConflictNoteId} />
          <ConflictFieldNote conflict={conflict} fields={imageConflictFields} id={imageConflictNoteId} />
          <ConflictNotice conflict={conflict} refreshError={conflictRefreshError} />
          {unresolvedFields.filter((field) => conflictFields.includes(field)).map((field) => {
            const isImageField = field === 'reportImages' || field === 'resolutionReportImages';
            return (
              <ConflictFieldResolution
                key={field}
                field={field}
                localValue={isImageField ? currentPayload.images : currentPayload.text}
                latestValue={isImageField ? latestImages : latestText}
                descriptionId={`report-resolution-${ticket.id}-${field}`}
                onKeep={() => onConflictReview?.(field === 'resolutionNotes' || field === 'postResolutionReport' ? ['resolutionNotes', 'postResolutionReport'] : [field])}
                onUseLatest={() => useLatestReportField(field)}
              />
            );
          })}
          <ConflictSnapshotReview conflict={conflict} localTicket={ticket} latestTicket={latestTicket} excludeFields={conflictFields} onReview={onConflictReview} />
          {isActionConflict ? (
            <Alert severity="warning" variant="outlined" role="status">
              {actionStillValid ? (
                <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
                  <span>{t('reportForm.closeStillValid', 'The ticket is still active. Reconfirm that you want to close the latest version.')}</span>
                  <Button type="button" size="small" variant={actionConfirmed ? 'contained' : 'outlined'} onClick={() => setActionConfirmed(true)}>
                    {actionConfirmed ? t('reportForm.closeReconfirmed', 'Close reconfirmed') : t('reportForm.reconfirmClose', 'Reconfirm close')}
                  </Button>
                </Stack>
              ) : t('reportForm.closeObsolete', 'This ticket is no longer active, so closing it is obsolete. Cancel this action and review the latest ticket.')}
            </Alert>
          ) : null}
          {error && !conflict ? <Alert severity="error" role="alert">{error}</Alert> : null}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button type="button" variant="outlined" onClick={onClose}>{t('common.cancel', 'Cancel')}</Button>
        <Button type="submit" form="bug-report-action-form" disabled={submitting || !currentPayload.text.trim() || unresolvedFields.length > 0 || (isActionConflict && (!actionConfirmed || !actionStillValid))}>
          {submitting ? t('common.saving', 'Saving...') : submitLabel}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
