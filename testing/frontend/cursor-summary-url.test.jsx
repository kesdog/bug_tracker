import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '../../react/node_modules/@testing-library/react/dist/index.js';
import DashboardPage from '../../react/src/pages/DashboardPage';
import ViewTicketsPage from '../../react/src/pages/ViewTicketsPage';
import { TICKET_PAGE_SIZE_STORAGE_KEY } from '../../react/src/components/TicketTable';
import { readAppUrlState, writeAppUrlState } from '../../react/src/url_state';

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
  window.history.replaceState({}, '', '/');
});

describe('cursor queues, exact summary and shareable state', () => {
  it('uses cursor transitions and stores the user-scoped page size', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url) => {
      const parsed = new URL(String(url), window.location.origin);
      if (parsed.searchParams.get('cursor') === 'next-25') {
        return { ok: true, json: async () => ({ items: [{ id: 'page-2', issueTitle: 'Second cursor page' }], totalCount: 26, nextCursor: null, hasMore: false }) };
      }
      return { ok: true, json: async () => ({ items: [{ id: 'page-1', issueTitle: 'First cursor page' }], totalCount: 26, nextCursor: 'next-25', hasMore: true }) };
    });

    render(<ViewTicketsPage token="token" userRole="dev" currentUserId="u-9" />);
    expect(await screen.findByText('First cursor page')).toBeInTheDocument();
    expect(screen.getByText(/1–25 of 26/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /go to next page/i }));
    expect(await screen.findByText('Second cursor page')).toBeInTheDocument();
    expect(fetchSpy.mock.calls.some(([url]) => new URL(String(url), window.location.origin).searchParams.get('cursor') === 'next-25')).toBe(true);
    expect(localStorage.getItem(`${TICKET_PAGE_SIZE_STORAGE_KEY}:u-9`)).toBe('25');
    expect(screen.getByRole('combobox', { name: /rows per page/i })).toBeInTheDocument();
  });

  it('renders exact summary metrics instead of deriving them from the preview', () => {
    render(<DashboardPage tickets={[]} summary={{ activeTotal: 81, allocatedToMe: 7, visibleProjects: 4, urgentActive: 12, unassignedActive: 9 }} allocatedCount={7} />);
    const summary = screen.getByLabelText(/dashboard summary/i);
    expect(summary).toHaveTextContent('81');
    expect(summary).toHaveTextContent('12');
    expect(summary).toHaveTextContent('9');
    expect(summary).not.toHaveTextContent('Visible projects');
  });

  it('restores filters from URL and deep-opens a ticket by ID', async () => {
    window.history.replaceState({}, '', '/?view=tickets&q=checkout&quick=urgent&priority=p0&ticket=bug-deep');
    expect(readAppUrlState()).toMatchObject({ view: 'tickets', search: 'checkout', quick: 'urgent', ticket: 'bug-deep', filters: expect.objectContaining({ priority: 'p0' }) });
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url) => String(url).endsWith('/api/bugs/bug-deep')
      ? { ok: true, json: async () => ({ id: 'bug-deep', issueTitle: 'Deep ticket', status: 'todo', description: 'Opened from URL' }) }
      : { ok: true, json: async () => ({ items: [], totalCount: 0, nextCursor: null, hasMore: false }) });
    render(<ViewTicketsPage token="token" userRole="dev" initialSearch="checkout" initialQuickFilter="urgent" initialFilters={{ priority: 'p0' }} initialTicketId="bug-deep" />);
    expect(await screen.findByText('Opened from URL')).toBeInTheDocument();
    expect(fetchSpy.mock.calls.some(([url]) => String(url).endsWith('/api/bugs/bug-deep'))).toBe(true);
    writeAppUrlState({ view: 'archived', search: 'fixed' });
    expect(window.location.search).toContain('view=archived');
    expect(window.location.search).toContain('q=fixed');
  });
});
