export function stripLeadingDescription(solutionSteps, description) {
  const normalizedSolution = solutionSteps.trim();
  const normalizedDescription = description.trim();
  if (!normalizedSolution || !normalizedDescription) return normalizedSolution;
  if (normalizedSolution === normalizedDescription) return '';
  if (normalizedSolution.startsWith(normalizedDescription)) return normalizedSolution.slice(normalizedDescription.length).replace(/^\s+/, '');
  return normalizedSolution;
}

export function getText(value) {
  return typeof value === 'string' ? value.trim() : '';
}

export function getInitialReportText(ticket) {
  const description = getText(ticket?.description);
  if (description) return description;
  const solutionText = getSolutionReportText(ticket);
  if (!solutionText) return getText(ticket?.report);
  return '';
}

export function getSolutionReportText(ticket) {
  if (!ticket) return '';
  const bugDescription = getText(ticket.description);
  const rawSolutionSteps = getText(ticket.postResolutionReport) || getText(ticket.resolutionNotes);
  return stripLeadingDescription(rawSolutionSteps, bugDescription);
}

export function getInitialReportImages(ticket) {
  return Array.isArray(ticket?.reportImages) ? ticket.reportImages : [];
}

export function getSolutionReportImages(ticket) {
  if (Array.isArray(ticket?.resolutionReportImages)) return ticket.resolutionReportImages;
  const initialText = getInitialReportText(ticket);
  const solutionText = getSolutionReportText(ticket);
  return !initialText && solutionText ? getInitialReportImages(ticket) : [];
}

export function parseSqlDate(value) {
  if (!value || typeof value !== 'string') return null;
  const normalized = value.includes('T') ? value : value.replace(' ', 'T');
  const parsed = new Date(`${normalized}Z`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

export function getActiveDurationLabel(ticket) {
  const assignedAt = parseSqlDate(ticket?.assignedAt);
  if (!assignedAt) return ticket?.status === 'closed' ? 'Not recorded' : 'Not active yet';
  const end = parseSqlDate(ticket?.closeDate) || new Date();
  const totalMinutes = Math.floor(Math.max(0, end.getTime() - assignedAt.getTime()) / (1000 * 60));
  const days = Math.floor(totalMinutes / (60 * 24));
  const hours = Math.floor((totalMinutes % (60 * 24)) / 60);
  return `${days}d ${hours}h ${totalMinutes % 60}m`;
}

export function hasReportContent(section) {
  return Boolean(section.text.trim()) || section.images.length > 0;
}

export function identityFrom(ticket, name, fallbackId = '') {
  if (name === 'projectOwner') return (ticket?.contacts || []).find((contact) => contact.kinds?.includes('owner')) || null;
  if (name === 'resolvedBy' && ticket?.resolver) return ticket.resolver;
  const direct = ticket?.[name] || ticket?.[`${name}Identity`];
  if (direct && typeof direct === 'object') return direct;
  const userId = fallbackId || ticket?.[`${name}UserId`] || '';
  return (ticket?.contacts || []).find((contact) => contact.userId === userId) || (userId ? { userId } : null);
}

export function identityLabel(identity) {
  return identity?.username || identity?.displayName || identity?.userId || 'Unknown user';
}
