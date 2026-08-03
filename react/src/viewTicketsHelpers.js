export function filterTicketsByQuickFilter(tickets, quickFilter) {
  if (quickFilter === 'urgent') return tickets.filter((ticket) => ticket.severity === 'urgent' || ticket.priority === 'p0');
  if (quickFilter === 'unassigned') return tickets.filter((ticket) => !ticket.assigneeUserId);
  if (quickFilter === 'recently-updated') {
    return [...tickets]
      .sort((a, b) => String(b.updatedAt || '').localeCompare(String(a.updatedAt || '')))
      .slice(0, 20);
  }
  return tickets;
}
