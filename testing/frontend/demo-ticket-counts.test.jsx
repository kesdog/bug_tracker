import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor, within } from '../../react/node_modules/@testing-library/react/dist/index.js';
import ArchivedPage from '../../react/src/pages/ArchivedPage';
import ViewTicketsPage from '../../react/src/pages/ViewTicketsPage';
import { TICKET_PAGE_SIZE_STORAGE_KEY } from '../../react/src/components/TicketTable';

const DEMO_ACTIVE_COUNT = 45;
const DEMO_CLOSED_COUNT = 15;

function demoTickets(count, status) {
  return Array.from({ length: count }, (_, index) => ({
    id: `demo-${status}-${index + 1}`,
    issueTitle: `Demo ${status} ticket ${index + 1}`,
    status,
    reporterUserId: 'usr_dev_001',
    assigneeUserId: status === 'closed' ? 'usr_dev_002' : null,
    createdAt: `2026-07-${String((index % 28) + 1).padStart(2, '0')} 10:00:00`,
    closeDate: status === 'closed' ? '2026-07-31 12:00:00' : null,
    projectId: `project-${index % 5}`,
    projectName: `Project ${index % 5}`,
    bugType: 'api',
    severity: 'mid',
    priority: 'p2',
    tags: ['demo'],
    version: 1
  }));
}

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
});

describe('demo ticket table completeness', () => {
  it.each([
    ['active', DEMO_ACTIVE_COUNT, ViewTicketsPage],
    ['closed', DEMO_CLOSED_COUNT, ArchivedPage]
  ])('renders every %s demo ticket for an admin', async (status, expectedCount, Page) => {
    const userId = `usr_admin_${status}`;
    localStorage.setItem(`${TICKET_PAGE_SIZE_STORAGE_KEY}:${userId}`, '100');
    const items = demoTickets(expectedCount, status === 'active' ? 'open' : 'closed');
    const fetchSpy = vi.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({ items, totalCount: expectedCount, nextCursor: null, hasMore: false })
    });

    render(<Page token="admin-token" userRole="admin" currentUserId={userId} />);

    const grid = await screen.findByRole('grid', { name: 'Ticket table' });
    await waitFor(() => expect(within(grid).getAllByRole('row')).toHaveLength(expectedCount + 1));
    expect(within(grid).queryByRole('columnheader', { name: /priority/i })).not.toBeInTheDocument();
    expect(screen.getByText(new RegExp(`1.${expectedCount} of ${expectedCount}`))).toBeInTheDocument();

    const requestUrl = new URL(String(fetchSpy.mock.calls[0][0]), window.location.origin);
    expect(requestUrl.searchParams.get('status')).toBe(status);
    expect(requestUrl.searchParams.get('limit')).toBe('100');
    expect(requestUrl.searchParams.get('projectId')).toBeNull();
    expect(requestUrl.searchParams.get('assigneeUserId')).toBeNull();
  });
});
