import { fetchBugById } from './api/bugs';

const FIELD_LABELS = {
  assigneeUserId: 'assignee',
  bugType: 'bug type',
  description: 'bug report',
  issueTitle: 'issue title',
  postResolutionReport: 'solution steps',
  priority: 'priority',
  projectId: 'project',
  reportImages: 'bug report images',
  resolutionNotes: 'solution steps',
  resolutionReportImages: 'solution images',
  severity: 'severity',
  status: 'status',
  tags: 'tags'
};

export function conflictFieldLabel(field) {
  return FIELD_LABELS[field] || field;
}

export function formatConflictValue(value) {
  if (value === null || value === undefined || value === '') return 'Not set';
  if (Array.isArray(value)) {
    if (value.length === 0) return 'None';
    return value.map((item) => typeof item === 'object' ? item.name || item.id || 'Image' : String(item)).join(', ');
  }
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

export function isTicketVersionConflict(error) {
  return error?.status === 409 && error?.errorCode === 'ticket_version_conflict';
}

export function conflictFields(conflict) {
  return Array.isArray(conflict?.changedFields) ? conflict.changedFields : [];
}

export function hasConflictField(conflict, names) {
  const expected = Array.isArray(names) ? names : [names];
  return expected.some((name) => conflictFields(conflict).includes(name));
}

export function clearReviewedConflictFields(conflict, names) {
  if (!conflict) {
    return null;
  }

  const reviewed = new Set(Array.isArray(names) ? names : [names]);
  return { ...conflict, changedFields: conflictFields(conflict).filter((field) => !reviewed.has(field)) };
}

export function conflictGuidance(conflict, { bulk = false } = {}) {
  const fields = conflictFields(conflict);
  const labels = fields.map((field) => FIELD_LABELS[field] || field);
  const changed = labels.length > 0 ? ` Changed: ${labels.join(', ')}.` : '';
  const prefix = bulk ? 'Some tickets changed before assignment.' : 'This ticket changed while you were editing.';
  return `${prefix}${changed} Review the latest details and submit again when ready.`;
}

export function conflictFromError(error) {
  return isTicketVersionConflict(error) ? { ...error.payload } : null;
}

export async function recoverTicketConflict(error, token) {
  const conflict = conflictFromError(error);
  if (!conflict) {
    return null;
  }

  try {
    const latestTicket = await fetchBugById(token, conflict.ticketId);
    return { conflict, latestTicket, refreshError: '' };
  } catch (refreshError) {
    return {
      conflict,
      latestTicket: null,
      refreshError: refreshError?.message || 'Unable to load the latest ticket.'
    };
  }
}

export function bulkConflictsFromError(error) {
  if (!isTicketVersionConflict(error)) {
    return [];
  }

  if (Array.isArray(error.payload?.conflicts)) {
    return error.payload.conflicts;
  }

  return error.payload?.ticketId ? [error.payload] : [];
}

export function bulkConflictsFromResult(result) {
  return (Array.isArray(result?.failed) ? result.failed : [])
    .filter((failure) => failure?.error === 'ticket_version_conflict' || failure?.conflict?.errorCode === 'ticket_version_conflict')
    .map((failure) => ({ ...failure.conflict, ticketId: failure.ticketId || failure.conflict?.ticketId }));
}
