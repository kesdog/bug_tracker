import React, { useEffect, useRef, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useTheme } from '@mui/material/styles';
import CloseIcon from '@mui/icons-material/Close';
import { downloadBugAttachment, requestTicketAccess } from '../api/bugs';
import { ActivityTimeline, ContactAction, MetadataBadges, ReportSection, StructuredReportDetails, TextEvidenceSection, TicketFocusCard } from './ReportPanelContent';
import { getActiveDurationLabel, getInitialReportImages, getInitialReportText, getSolutionReportImages, getSolutionReportText, hasReportContent, identityFrom, identityLabel } from './reportPanelUtils';

export default function ReportPanel({
  ticket,
  loading = false,
  error = '',
  onClose,
  actionLabel = '',
  onAction = null,
  onAddComment = null,
  title = 'Bug Details',
  showReportTabs = false,
  token = '',
  userType = 'human'
}) {
  const [activeReportId, setActiveReportId] = useState('initial');
  const [commentText, setCommentText] = useState('');
  const [commentSubmitting, setCommentSubmitting] = useState(false);
  const [commentError, setCommentError] = useState('');
  const [commentRecipient, setCommentRecipient] = useState(null);
  const [accessReason, setAccessReason] = useState('Please grant access so I can review and help resolve this ticket.');
  const [accessState, setAccessState] = useState({ submitting: false, message: '', error: '' });
  const commentInputRef = useRef(null);
  const [downloadingIds, setDownloadingIds] = useState(() => new Set());
  const [downloadError, setDownloadError] = useState('');
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('sm'));
  const activeDurationLabel = getActiveDurationLabel(ticket);

  useEffect(() => {
    setActiveReportId('initial');
    setCommentText('');
    setCommentError('');
    setCommentRecipient(null);
    setDownloadingIds(new Set());
    setDownloadError('');
  }, [ticket?.id]);

  if (!ticket && !loading && !error) return null;

  const attachments = Array.isArray(ticket?.attachments) ? ticket.attachments : [];
  const reportSections = ticket ? [
    {
      id: 'initial', label: 'Initial Bug Report', dateLabel: 'Submitted', date: ticket.createdAt,
      text: getInitialReportText(ticket), images: getInitialReportImages(ticket),
      emptyMessage: 'No initial bug report is available for this ticket.',
      attachments: attachments.filter((attachment) => attachment.purpose === 'initial-report'),
      attachmentLabel: 'Initial report attachments'
    },
    {
      id: 'solution', label: 'Solution / Fix Report', dateLabel: ticket.closeDate ? 'Resolved' : 'Updated', date: ticket.closeDate || ticket.updatedAt,
      text: getSolutionReportText(ticket), images: getSolutionReportImages(ticket),
      emptyMessage: 'No solution or fix report has been added yet.',
      attachments: attachments.filter((attachment) => attachment.purpose === 'solution-report' || attachment.purpose === 'close-report'),
      attachmentLabel: 'Solution / close attachments'
    }
  ] : [];
  const activeSection = reportSections.find((section) => section.id === activeReportId) || reportSections[0];
  const sectionsToRender = showReportTabs ? [activeSection].filter(Boolean) : reportSections.filter((section) => section.id === 'initial' || hasReportContent(section) || section.attachments.length > 0);

  async function handleDownload(attachment) {
    setDownloadingIds((current) => new Set(current).add(attachment.id));
    setDownloadError('');
    try {
      const result = await downloadBugAttachment(token, ticket.id, attachment.id, attachment.name || 'attachment');
      const objectUrl = URL.createObjectURL(result.blob);
      const link = document.createElement('a');
      link.href = objectUrl;
      link.download = result.filename;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(objectUrl);
    } catch (err) {
      setDownloadError(err.message || 'Unable to download attachment.');
    } finally {
      setDownloadingIds((current) => {
        const next = new Set(current);
        next.delete(attachment.id);
        return next;
      });
    }
  }

  async function submitComment(event) {
    event.preventDefault();
    if (!ticket || !onAddComment || !commentText.trim()) return;
    setCommentSubmitting(true);
    setCommentError('');
    try {
      await onAddComment(ticket.id, commentText.trim(), commentRecipient?.userId || '');
      setCommentText('');
      setCommentRecipient(null);
    } catch (err) {
      setCommentError(err.message || 'Unable to add comment.');
    } finally {
      setCommentSubmitting(false);
    }
  }

  function contactInTicket(identity) {
    const username = identity?.username || identity?.userId;
    setCommentRecipient(identity || null);
    setCommentText((current) => current || (username ? `@${username} ` : ''));
    window.setTimeout(() => commentInputRef.current?.focus(), 0);
  }

  async function submitAccessRequest() {
    const path = error?.requestAccessPath || error?.payload?.requestAccessPath;
    if (!path || !accessReason.trim()) return;
    setAccessState({ submitting: true, message: '', error: '' });
    try {
      await requestTicketAccess(token, path, accessReason.trim());
      setAccessState({ submitting: false, message: 'Project access request submitted.', error: '' });
    } catch (err) {
      setAccessState({ submitting: false, message: '', error: err.message || 'Unable to request access.' });
    }
  }

  const errorMessage = typeof error === 'string' ? error : error?.message || '';
  const permissionPayload = error?.errorCode === 'ticket_access_denied' ? (error.payload || error) : null;
  const statusExplanations = { todo: 'Awaiting assignment. Work has not started.', open: 'Assigned and in progress.', reopened: 'Returned to active work after closure.', closed: 'Resolved and archived.', cancelled: 'Cancelled without a solution report and archived.' };
  const reporter = ticket ? identityFrom(ticket, 'reporter', ticket.reporterUserId) : null;
  const assignee = ticket ? identityFrom(ticket, 'assignee', ticket.assigneeUserId) : null;

  return (
    <Dialog open onClose={onClose} aria-labelledby="report-panel-title" maxWidth="lg" scroll="paper" fullScreen={isMobile}>
      <DialogTitle id="report-panel-title" sx={{ pr: 7 }}>
        <Typography component="span" variant="h5" sx={{ fontWeight: 900 }}>{title}</Typography>
        {ticket ? <Typography className="report-ticket-title" color="text.secondary">{ticket.issueTitle}</Typography> : null}
        {!showReportTabs && ticket ? <Typography className="report-open-since" variant="body2" color="text.secondary">Active Time: {activeDurationLabel}</Typography> : null}
        <IconButton type="button" className="report-close" aria-label="Close report" onClick={onClose} sx={{ position: 'absolute', top: 12, right: 12 }}><CloseIcon /></IconButton>
      </DialogTitle>
      <DialogContent dividers>
        {loading ? <CircularProgress aria-label="loading report details" size={24} /> : null}
        {errorMessage ? <Alert severity="error" role="alert" sx={{ my: 1 }}>{errorMessage}</Alert> : null}
        {permissionPayload ? (
          <Card variant="outlined" component="section" aria-label="Ticket access help" sx={{ my: 2 }}><CardContent>
            <Typography component="h3" variant="h6">You do not currently have access</Typography>
            {Array.isArray(permissionPayload.steps) && permissionPayload.steps.length > 0 ? <Box component="ol">{permissionPayload.steps.map((step) => <li key={step}>{step}</li>)}</Box> : null}
            {Array.isArray(permissionPayload.contacts) && permissionPayload.contacts.length > 0 ? <Typography>Contact: {permissionPayload.contacts.map((contact) => `${identityLabel(contact)}${contact.role ? ` (${contact.role})` : ''}`).join(', ')}</Typography> : null}
            {userType !== 'agent' && permissionPayload.requestAccessPath ? <Stack spacing={1.25} sx={{ mt: 2 }}><TextField label="Access request reason" value={accessReason} onChange={(event) => setAccessReason(event.target.value)} multiline rows={2} /><Button onClick={submitAccessRequest} disabled={accessState.submitting || !accessReason.trim()}>{accessState.submitting ? 'Requesting…' : 'Request project access'}</Button></Stack> : null}
            {accessState.message ? <Alert severity="success" sx={{ mt: 1 }}>{accessState.message}</Alert> : null}
            {accessState.error ? <Alert severity="error" sx={{ mt: 1 }}>{accessState.error}</Alert> : null}
          </CardContent></Card>
        ) : null}
        {!loading && !error && ticket ? (
          <>
            {showReportTabs ? <TicketFocusCard ticket={ticket} activeDurationLabel={activeDurationLabel} onContactInTicket={contactInTicket} /> : (
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} className="report-meta" sx={{ alignItems: { sm: 'center' }, flexWrap: 'wrap' }}>
                <Stack component="span" direction="row" spacing={0.5} sx={{ alignItems: 'center' }}><span>Reported By: {identityLabel(reporter)}</span><ContactAction identity={reporter} ticket={ticket} onContactInTicket={contactInTicket} /></Stack>
                <Stack component="span" direction="row" spacing={0.5} sx={{ alignItems: 'center' }}><span>Assigned: {ticket.assigneeUserId ? identityLabel(assignee) : '-'}</span><ContactAction identity={assignee} ticket={ticket} onContactInTicket={contactInTicket} /></Stack>
                <span>Status: {ticket.status || '-'}</span>
              </Stack>
            )}
            <MetadataBadges ticket={ticket} />
            <Alert severity={ticket.status === 'closed' ? 'success' : ticket.status === 'cancelled' || ticket.status === 'reopened' ? 'warning' : 'info'} icon={false} sx={{ my: 1 }}><strong>Current status: {ticket.status || 'unknown'}.</strong> {statusExplanations[ticket.status] || 'Current workflow state.'}</Alert>
            {ticket.cancellationReason ? <Alert severity="warning" icon={false} sx={{ my: 1 }}><strong>Cancellation reason:</strong> {ticket.cancellationReason}</Alert> : null}
            {onAction && actionLabel ? <Button type="button" className="report-link-button" onClick={() => onAction(ticket)} sx={{ my: 1 }}>{actionLabel}</Button> : null}
            {showReportTabs ? <Tabs value={activeReportId} onChange={(event, value) => setActiveReportId(value)} aria-label="Archived ticket reports" variant="scrollable" scrollButtons="auto" allowScrollButtonsMobile sx={{ borderBottom: 1, borderColor: 'divider', mb: 2 }}>{reportSections.map((section) => <Tab key={section.id} value={section.id} label={section.label} />)}</Tabs> : null}
            <Box className="report-sections">{sectionsToRender.map((section) => <ReportSection key={section.id} section={section} downloadingIds={downloadingIds} onDownload={handleDownload} />)}</Box>
            {downloadError ? <Alert severity="error" role="alert" sx={{ mt: 2 }}>{downloadError}</Alert> : null}
            <StructuredReportDetails ticket={ticket} />
            <TextEvidenceSection evidence={ticket.textEvidence} />
            <ActivityTimeline ticket={ticket} activity={ticket.activity} onContactInTicket={contactInTicket} />
            {onAddComment ? (
              <form className="comment-form" onSubmit={submitComment}>
                <TextField id={`comment-${ticket.id}`} inputRef={commentInputRef} label="Add comment" value={commentText} onChange={(event) => setCommentText(event.target.value)} rows={3} multiline fullWidth placeholder="Add a plain-text update for this ticket" slotProps={{ htmlInput: { maxLength: 2000 } }} />
                {commentRecipient ? <Typography variant="caption" color="text.secondary">Sending this comment to {identityLabel(commentRecipient)}.</Typography> : null}
                <Button type="submit" disabled={commentSubmitting || !commentText.trim()} sx={{ mt: 1 }}>{commentSubmitting ? 'Adding...' : 'Add Comment'}</Button>
                {commentError ? <Alert severity="error" role="alert" sx={{ mt: 1 }}>{commentError}</Alert> : null}
              </form>
            ) : null}
          </>
        ) : null}
      </DialogContent>
      <DialogActions><Button type="button" variant="outlined" onClick={onClose}>Close</Button></DialogActions>
    </Dialog>
  );
}
