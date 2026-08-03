import React, { useState } from 'react';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import AddCircleOutlineIcon from '@mui/icons-material/AddCircleOutlined';
import AssignmentIndIcon from '@mui/icons-material/AssignmentInd';
import AttachFileIcon from '@mui/icons-material/AttachFile';
import ChatBubbleOutlineIcon from '@mui/icons-material/ChatBubbleOutlineOutlined';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutlined';
import DownloadIcon from '@mui/icons-material/Download';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import MailOutlineIcon from '@mui/icons-material/MailOutlined';
import ReplayIcon from '@mui/icons-material/Replay';
import Timeline from '@mui/lab/Timeline';
import TimelineConnector from '@mui/lab/TimelineConnector';
import TimelineContent from '@mui/lab/TimelineContent';
import TimelineDot from '@mui/lab/TimelineDot';
import TimelineItem from '@mui/lab/TimelineItem';
import TimelineOppositeContent from '@mui/lab/TimelineOppositeContent';
import TimelineSeparator from '@mui/lab/TimelineSeparator';
import { ReportBuilder } from '../report_builder';
import { formatTicketDate, getProjectName } from '../table_utils';
import { PriorityChip, SeverityChip, StatusChip, TagChip } from './MuiPrimitives';
import { getText, identityFrom, identityLabel, parseSqlDate } from './reportPanelUtils';

const sectionHeadingSx = {
  color: 'text.primary',
  fontSize: { xs: '1.15rem', sm: '1.25rem' },
  fontWeight: 800,
  letterSpacing: '-0.015em',
  lineHeight: 1.25
};

const sectionHeadingRowSx = {
  pb: 1.25,
  mb: 2,
  borderBottom: 1,
  borderColor: 'divider'
};

function DetailTile({ label, value, action = null }) {
  return <Paper variant="outlined" sx={(theme) => ({ p: 1.5, borderRadius: 2.5, bgcolor: 'rgba(255,255,255,0.58)', ...theme.applyStyles('dark', { bgcolor: 'rgba(15, 23, 42, 0.58)' }) })}><Typography variant="overline" color="text.secondary" sx={{ fontWeight: 900, letterSpacing: '0.08em' }}>{label}</Typography><Stack direction="row" spacing={0.5} sx={{ mt: 0.35, alignItems: 'center', justifyContent: 'space-between' }}><Typography sx={{ fontWeight: 800, overflowWrap: 'anywhere' }}>{value || '-'}</Typography>{action}</Stack></Paper>;
}

function AttachmentGroup({ attachments, label, downloadingIds, onDownload }) {
  if (attachments.length === 0) return null;
  return <Box component="section" aria-label={label} sx={{ mt: 2 }}><Typography component="h4" variant="subtitle1" sx={{ mb: 1, color: 'text.primary', fontWeight: 800 }}>{label}</Typography><Stack spacing={1}>{attachments.map((attachment) => <Paper key={attachment.id} variant="outlined" sx={{ p: 1.25, display: 'flex', gap: 1, alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap' }}><Box><Typography sx={{ fontWeight: 800, overflowWrap: 'anywhere' }}>{attachment.name}</Typography><Typography variant="caption" color="text.secondary">{attachment.contentType || 'image'}{Number.isFinite(attachment.sizeBytes) ? ` · ${(attachment.sizeBytes / 1024).toFixed(1)} KiB` : ''}</Typography></Box><Button type="button" size="small" startIcon={<DownloadIcon />} disabled={downloadingIds.has(attachment.id)} onClick={() => onDownload(attachment)}>{downloadingIds.has(attachment.id) ? 'Downloading…' : 'Download'}</Button></Paper>)}</Stack></Box>;
}

export function ReportSection({ section, downloadingIds, onDownload }) {
  const reportBlocks = ReportBuilder.fromSerialized(section.text, section.images).blocks;
  const hasRenderableContent = reportBlocks.some((block) => block.type === 'image' ? Boolean(block.image?.dataUrl) : Boolean(block.text && block.text.trim()));
  return <Card component="section" variant="outlined" aria-label={section.label} sx={{ boxShadow: 'none' }}><CardContent><Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ ...sectionHeadingRowSx, justifyContent: 'space-between', alignItems: { xs: 'flex-start', sm: 'baseline' } }}><Typography component="h3" variant="h6" sx={sectionHeadingSx}>{section.label}</Typography><Chip size="small" variant="outlined" label={`${section.dateLabel}: ${formatTicketDate(section.date)}`} /></Stack><Stack spacing={1.5} className="report-body">{!hasRenderableContent ? <Typography color="text.secondary">{section.emptyMessage}</Typography> : null}{reportBlocks.map((block) => block.type === 'image' ? <figure className="report-image-frame" key={block.id}><img className="report-image" src={block.image?.dataUrl} alt={block.image?.name || 'report image'} loading="lazy" /><figcaption className="report-image-name">{block.image?.name || 'Image'}</figcaption></figure> : <Typography key={block.id} className="report-text-block" sx={{ lineHeight: 1.65 }}>{block.text || ''}</Typography>)}</Stack><AttachmentGroup attachments={section.attachments} label={section.attachmentLabel} downloadingIds={downloadingIds} onDownload={onDownload} /></CardContent></Card>;
}

export function ContactAction({ identity, ticket, onContactInTicket }) {
  const [anchor, setAnchor] = useState(null);
  const menuId = React.useId();
  if (!identity?.userId && !identity?.username) return null;
  const label = identityLabel(identity);
  const ticketUrl = typeof window === 'undefined' ? '' : new URL(`?view=tickets&ticket=${encodeURIComponent(ticket.id)}`, window.location.origin + window.location.pathname).toString();
  const subject = `[Bug Tracker] ${ticket.id}: ${ticket.issueTitle || 'Untitled ticket'}`;
  const mailto = identity.email ? `mailto:${encodeURIComponent(identity.email)}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(ticketUrl)}` : '';
  if (!onContactInTicket && !mailto) return null;
  return <><Tooltip title={`Contact ${label}`}><IconButton size="small" color="primary" aria-label={`Contact ${label}`} aria-haspopup="menu" aria-expanded={Boolean(anchor)} aria-controls={anchor ? menuId : undefined} onClick={(event) => setAnchor(event.currentTarget)} sx={{ border: 1, borderColor: 'divider' }}><MailOutlineIcon fontSize="small" /></IconButton></Tooltip><Menu id={menuId} anchorEl={anchor} open={Boolean(anchor)} onClose={() => setAnchor(null)} slotProps={{ list: { 'aria-label': `Contact ${label}` } }}>{onContactInTicket ? <MenuItem onClick={() => { setAnchor(null); onContactInTicket(identity); }}>Contact in ticket</MenuItem> : null}{mailto ? <MenuItem component="a" href={mailto} onClick={() => setAnchor(null)}>Email {label}</MenuItem> : null}</Menu></>;
}

export function TicketFocusCard({ ticket, activeDurationLabel, onContactInTicket }) {
  const reporter = identityFrom(ticket, 'reporter', ticket.reporterUserId);
  const assignee = identityFrom(ticket, 'assignee', ticket.assigneeUserId);
  const resolver = identityFrom(ticket, 'resolvedBy', ticket.resolvedByUserId);
  const owner = identityFrom(ticket, 'projectOwner', ticket.projectOwnerUserId);
  const identityTile = (label, identity) => <DetailTile label={label} value={identity ? `${identityLabel(identity)}${identity.username && identity.userId && label !== 'Resolved By' && label !== 'Project Owner' ? ` (${identity.userId})` : ''}` : '-'} action={<ContactAction identity={identity} ticket={ticket} onContactInTicket={onContactInTicket} />} />;
  return <Card component="aside" aria-label="Ticket summary" sx={{ mb: 2 }}><CardContent><Typography variant="overline" color="primary" sx={{ fontWeight: 900, letterSpacing: '0.1em' }}>Ticket Summary</Typography><Typography component="h3" variant="h5" sx={{ mt: 0.5, mb: 1.5, fontWeight: 900 }}>{ticket.issueTitle || 'Untitled ticket'}</Typography><Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mb: 2 }}><StatusChip value={ticket.status} /><SeverityChip value={ticket.severity} /><PriorityChip value={ticket.priority || 'p2'} /></Stack><Box sx={{ display: 'grid', gap: 1.25, gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' } }}><DetailTile label="Project" value={getProjectName(ticket)} />{identityTile('Reporter', reporter)}{identityTile('Assignee', assignee)}<DetailTile label="Active Time" value={activeDurationLabel} /><DetailTile label="Created" value={formatTicketDate(ticket.createdAt)} /><DetailTile label="Assigned" value={formatTicketDate(ticket.assignedAt)} /><DetailTile label="Updated" value={formatTicketDate(ticket.updatedAt)} />{identityTile('Resolved By', resolver)}{owner ? identityTile('Project Owner', owner) : null}</Box></CardContent></Card>;
}

function buildTimelineItems(ticket, activity) {
  const persisted = Array.isArray(activity) ? activity : [];
  const items = [];
  if (persisted.length === 0 && ticket?.createdAt) items.push({ id: 'created', kind: 'created', title: 'Ticket created', body: ticket.reporterUserId ? `Reported by ${ticket.reporterUserId}` : 'Initial report submitted.', createdAt: ticket.createdAt, actor: ticket.reporterUserId || 'reporter', color: 'info', changedFields: [] });
  if (persisted.length === 0 && ticket?.assignedAt) items.push({ id: 'assigned', kind: 'assigned', title: 'Ticket became active', body: ticket.assigneeUserId ? `Assigned to ${ticket.assigneeUserId}` : 'Ticket moved into active work.', createdAt: ticket.assignedAt, actor: ticket.assigneeUserId || 'system', color: 'primary', changedFields: [] });
  if (persisted.length === 0 && ticket?.updatedAt && ticket.updatedAt !== ticket.createdAt && ticket.updatedAt !== ticket.assignedAt) items.push({ id: 'updated', kind: 'updated', title: 'Last updated', body: 'Ticket details or report content changed.', createdAt: ticket.updatedAt, actor: 'system', color: 'secondary', changedFields: [] });
  if (persisted.length === 0 && ticket?.closeDate) items.push({ id: 'closed', kind: 'closed', title: 'Ticket resolved', body: ticket.resolvedByUserId ? `Resolved by ${ticket.resolvedByUserId}` : 'Ticket was closed.', createdAt: ticket.closeDate, actor: ticket.resolvedByUserId || ticket.assigneeUserId || 'system', color: 'success', changedFields: [] });
  const definitions = { created: ['Ticket created', 'info', AddCircleOutlineIcon], assigned: ['Ticket assigned', 'primary', AssignmentIndIcon], edited: ['Ticket edited', 'secondary', EditOutlinedIcon], attachment_added: ['Attachment added', 'info', AttachFileIcon], comment: ['Comment added', 'secondary', ChatBubbleOutlineIcon], closed: ['Ticket closed', 'success', CheckCircleOutlineIcon], reopened: ['Ticket reopened', 'warning', ReplayIcon] };
  persisted.forEach((item) => {
    const kind = item.kind || item.type || 'comment';
    const definition = definitions[kind] || [kind === 'system' ? 'System activity' : 'Activity recorded', 'warning', EditOutlinedIcon];
    const actorIdentity = item.actor || item.actorIdentity || identityFrom(ticket, '', item.actorUserId);
    items.push({ id: item.id, kind, title: item.label || definition[0], body: item.body, createdAt: item.createdAt, actor: identityLabel(actorIdentity || { userId: item.actorUsername || item.actorUserId || 'system' }), actorId: actorIdentity?.userId || item.actorUserId || '', actorIdentity, actorType: actorIdentity?.userType || item.actorType || 'human', color: definition[1], Icon: definition[2], changedFields: Array.isArray(item.changedFields) ? item.changedFields : [], version: item.ticketVersion ?? item.version, transition: item.transitionLabel || (item.fromStatus && item.toStatus ? `${item.fromStatus} → ${item.toStatus}` : ''), subjectIdentity: item.subject || item.subjectIdentity || identityFrom(ticket, '', item.subjectUserId) });
  });
  return items.sort((a, b) => (parseSqlDate(a.createdAt)?.getTime() || 0) - (parseSqlDate(b.createdAt)?.getTime() || 0) || String(a.id || '').localeCompare(String(b.id || '')));
}

export function ActivityTimeline({ ticket, activity, onContactInTicket }) {
  const items = buildTimelineItems(ticket, activity);
  return <Card component="section" variant="outlined" aria-label="Ticket activity timeline" sx={{ mt: 2, boxShadow: 'none' }}><CardContent><Typography component="h3" variant="h6" sx={{ fontWeight: 900, mb: 1 }}>Ticket Timeline</Typography>{items.length === 0 ? <Typography color="text.secondary">No timeline activity has been recorded yet.</Typography> : null}{items.length > 0 ? <Timeline position="right" sx={{ m: 0, p: 0, '& .MuiTimelineItem-root:before': { display: 'none' } }}>{items.map((item, index) => <TimelineItem key={item.id}><TimelineSeparator><TimelineDot color={item.color || 'primary'} variant={item.kind === 'comment' ? 'outlined' : 'filled'}>{item.Icon ? <item.Icon fontSize="small" /> : null}</TimelineDot>{index < items.length - 1 ? <TimelineConnector /> : null}</TimelineSeparator><TimelineOppositeContent sx={{ display: { xs: 'none', sm: 'block' }, flex: 0.28, color: 'text.secondary', fontSize: '0.82rem' }}>{formatTicketDate(item.createdAt)}</TimelineOppositeContent><TimelineContent sx={{ pb: 2 }}><Paper variant="outlined" sx={(theme) => ({ p: 1.5, borderRadius: 2.5, bgcolor: 'rgba(255,255,255,0.58)', ...theme.applyStyles('dark', { bgcolor: 'rgba(15, 23, 42, 0.58)' }) })}><Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mb: 0.5, alignItems: 'center', justifyContent: 'space-between' }}><Typography sx={{ fontWeight: 900 }}>{item.title}</Typography><Chip size="small" variant="outlined" label={item.kind} /></Stack><Typography color="text.secondary" sx={{ display: { sm: 'none' }, mb: 0.5 }}>{formatTicketDate(item.createdAt)}</Typography><Typography sx={{ whiteSpace: 'pre-wrap' }}>{item.body || '-'}</Typography>{item.transition ? <Typography variant="body2" sx={{ fontWeight: 700 }}>{item.transition}</Typography> : null}{item.changedFields.length > 0 || item.version != null ? <Typography variant="caption" display="block" color="text.secondary">{item.changedFields.length > 0 ? `Changed: ${item.changedFields.join(', ')}` : ''}{item.version != null ? `${item.changedFields.length ? ' · ' : ''}Version ${item.version}` : ''}</Typography> : null}<Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}><Typography variant="caption" color="text.secondary">{item.actor}{item.actorId && item.actorId !== item.actor ? ` (${item.actorId})` : ''}{item.actorType ? ` · ${item.actorType}` : ''}{item.subjectIdentity ? ` · To ${identityLabel(item.subjectIdentity)}` : ''}</Typography><ContactAction identity={item.actorIdentity} ticket={ticket} onContactInTicket={onContactInTicket} /></Stack></Paper></TimelineContent></TimelineItem>)}</Timeline> : null}</CardContent></Card>;
}

export function MetadataBadges({ ticket }) {
  const tags = Array.isArray(ticket.tags) ? ticket.tags : [];
  return <Stack direction="row" spacing={1} useFlexGap className="report-badges" aria-label="Ticket priority and tags" sx={{ my: 1.5, flexWrap: 'wrap' }}><PriorityChip value={ticket.priority || 'p2'} />{tags.length > 0 ? tags.map((tag) => <TagChip key={tag} value={tag} />) : <TagChip value="" />}</Stack>;
}

export function StructuredReportDetails({ ticket }) {
  const rows = [['Environment', getText(ticket.environment)], ['Expected behavior', getText(ticket.expectedBehavior)], ['Actual behavior', getText(ticket.actualBehavior)], ['Steps to reproduce', getText(ticket.stepsToReproduce)], ['Frequency', getText(ticket.frequency)]].filter(([, value]) => value);
  if (rows.length === 0) return null;
  return <Card component="section" variant="outlined" aria-label="Ticket details" sx={{ mt: 2, boxShadow: 'none' }}><CardContent><Typography component="h3" variant="h6" sx={{ ...sectionHeadingSx, ...sectionHeadingRowSx }}>Ticket Details</Typography><Box component="dl" className="structured-report-list">{rows.map(([label, value]) => <DetailTile key={label} label={label} value={value} />)}</Box></CardContent></Card>;
}

export function TextEvidenceSection({ evidence }) {
  const items = Array.isArray(evidence) ? evidence : [];
  if (items.length === 0) return null;
  return <Card component="section" variant="outlined" aria-label="Text evidence" sx={{ mt: 2, boxShadow: 'none' }}><CardContent><Typography component="h3" variant="h6" sx={{ ...sectionHeadingSx, ...sectionHeadingRowSx }}>Text Evidence</Typography><Stack spacing={1} className="text-evidence-list">{items.map((item) => <details key={item.name} className="text-evidence-item"><summary>{item.name}</summary><pre>{item.text}</pre></details>)}</Stack></CardContent></Card>;
}
