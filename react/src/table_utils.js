export const DEFAULT_PROJECT_NAME = 'Bug Tracker MVP';

export const PROJECT_NAME_BY_TYPE = {
  page_not_loading: 'Web App',
  form_submission: 'Web App',
  crash: 'Web App',
  api: 'API Service',
  database: 'Database Service'
};

const SEVERITY_ORDER = {
  low: 1,
  mid: 2,
  high: 3,
  urgent: 4
};

const PRIORITY_ORDER = {
  p3: 1,
  p2: 2,
  p1: 3,
  p0: 4
};

const STATUS_ORDER = {
  todo: 1,
  open: 2,
  reopened: 3,
  closed: 4
};

export function getProjectName(ticket) {
  if (ticket.projectName) {
    return ticket.projectName;
  }

  return PROJECT_NAME_BY_TYPE[ticket.bugType] || DEFAULT_PROJECT_NAME;
}

export function formatTicketDate(value) {
  if (!value) {
    return '-';
  }

  const parsed = parseTicketDate(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  return parsed.toLocaleString();
}

export function parseTicketDate(value) {
  if (!value) {
    return new Date(Number.NaN);
  }

  if (value instanceof Date) {
    return value;
  }

  const normalized = String(value).includes('T') ? String(value) : String(value).replace(' ', 'T');
  const withTimezone = /(?:Z|[+-]\d{2}:?\d{2})$/.test(normalized) ? normalized : `${normalized}Z`;
  return new Date(withTimezone);
}

export function formatActiveSince(ticket) {
  const source = ticket?.assignedAt || ticket?.createdAt;
  if (!source) {
    return 'Active Since: -';
  }

  const parsed = parseTicketDate(source);
  if (Number.isNaN(parsed.getTime())) {
    return `Active Since: ${source}`;
  }

  const elapsedMinutes = Math.max(0, Math.floor((Date.now() - parsed.getTime()) / 60000));
  let value = elapsedMinutes;
  let unit = 'minute';

  if (elapsedMinutes >= 60 * 24) {
    value = Math.floor(elapsedMinutes / (60 * 24));
    unit = 'day';
  } else if (elapsedMinutes >= 60) {
    value = Math.floor(elapsedMinutes / 60);
    unit = 'hour';
  }

  return `Active Since: ${value} ${unit}${value === 1 ? '' : 's'}`;
}

export const TICKET_FIELD_ACCESSORS = {
  id: (ticket) => ticket.id || '',
  issueTitle: (ticket) => ticket.issueTitle || '',
  description: (ticket) => ticket.description || '',
  bugType: (ticket) => ticket.bugType || '',
  projectId: (ticket) => ticket.projectId || '',
  projectName: (ticket) => getProjectName(ticket),
  reporterUserId: (ticket) => ticket.reporterUserId || '',
  assigneeUserId: (ticket) => ticket.assigneeUserId || '',
  createdAt: (ticket) => ticket.createdAt || '',
  updatedAt: (ticket) => ticket.updatedAt || '',
  status: (ticket) => ticket.status || '',
  severity: (ticket) => ticket.severity || '',
  priority: (ticket) => ticket.priority || '',
  tags: (ticket) => Array.isArray(ticket.tags) ? ticket.tags.join(' ') : '',
  closeDate: (ticket) => ticket.closeDate || '',
  resolvedByUserId: (ticket) => ticket.resolvedByUserId || '',
  assignedAt: (ticket) => ticket.assignedAt || ticket.createdAt || '',
  resolutionNotes: (ticket) => ticket.resolutionNotes || '',
  postResolutionReport: (ticket) => ticket.postResolutionReport || '',
  resolvedBy: (ticket) => ticket.resolvedByUserId || ticket.assigneeUserId || '',
  resolvedReport: (ticket) => ticket.postResolutionReport || ticket.resolutionNotes || ''
};

function comparePrimitive(left, right) {
  if (typeof left === 'number' && typeof right === 'number') {
    return left - right;
  }

  return String(left).localeCompare(String(right));
}

export function getTicketSortValue(ticket, key) {
  if (key === 'severity') {
    return SEVERITY_ORDER[ticket.severity] || 0;
  }

  if (key === 'status') {
    return STATUS_ORDER[ticket.status] || 0;
  }

  if (key === 'priority') {
    return PRIORITY_ORDER[ticket.priority] || 0;
  }

  const accessor = TICKET_FIELD_ACCESSORS[key] || TICKET_FIELD_ACCESSORS.issueTitle;
  return accessor(ticket);
}

export function sortTickets(tickets, sortConfig) {
  const copy = [...tickets];
  const direction = sortConfig.direction === 'asc' ? 1 : -1;

  copy.sort((a, b) => comparePrimitive(getTicketSortValue(a, sortConfig.key), getTicketSortValue(b, sortConfig.key)) * direction);
  return copy;
}

export function nextSortConfig(current, key, defaultDirection = 'asc') {
  if (current.key === key) {
    return {
      key,
      direction: current.direction === 'asc' ? 'desc' : 'asc'
    };
  }

  return {
    key,
    direction: defaultDirection
  };
}

export function sortIndicator(sortConfig, key) {
  if (sortConfig.key !== key) {
    return '↕';
  }

  return sortConfig.direction === 'asc' ? '↑' : '↓';
}
