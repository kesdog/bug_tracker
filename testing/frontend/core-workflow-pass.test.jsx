import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '../../react/node_modules/@testing-library/react/dist/index.js';
import userEvent from '../../react/node_modules/@testing-library/user-event/dist/esm/index.js';
import { clearBugCache } from '../../react/src/api/bugs';
import NavBar from '../../react/src/components/NavBar';
import ArchivedPage from '../../react/src/pages/ArchivedPage';
import ViewTicketsPage from '../../react/src/pages/ViewTicketsPage';

afterEach(() => {
  vi.restoreAllMocks();
  clearBugCache();
});

function activeTicket(overrides = {}) {
  return {
    id: 'bug-001',
    issueTitle: 'Checkout crash',
    status: 'todo',
    reporterUserId: 'qa@example.com',
    assigneeUserId: '',
    createdAt: '2026-01-01 09:00:00',
    projectId: 'proj-store',
    projectName: 'Storefront',
    bugType: 'form_submission',
    severity: 'high',
    priority: 'p2',
    tags: ['front-end'],
    version: 7,
    ...overrides
  };
}

describe('core workflow pass frontend', () => {
  it('validates and submits ticket metadata edits', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
      const target = String(url);
      const method = options.method || 'GET';

      if (target.includes('/api/bugs?status=active') && method === 'GET') {
        return { ok: true, json: async () => [activeTicket()] };
      }

      if (target.endsWith('/api/bugs/bug-001/metadata') && method === 'PATCH') {
        return { ok: true, json: async () => ({ id: 'bug-001' }) };
      }

      throw new Error(`Unhandled fetch: ${method} ${target}`);
    });

    render(<ViewTicketsPage token="admin-token" userRole="admin" />);

    fireEvent.click(await screen.findByText(/checkout crash/i));
    fireEvent.click(screen.getByRole('menuitem', { name: /edit metadata/i }));
    const dialog = screen.getByRole('dialog', { name: /edit ticket metadata/i });
    fireEvent.change(within(dialog).getByLabelText(/issue title/i), { target: { value: '' } });
    fireEvent.click(within(dialog).getByRole('button', { name: /save metadata/i }));

    expect(screen.getByRole('alert')).toHaveTextContent(/issue title is required/i);

    fireEvent.change(within(dialog).getByLabelText(/issue title/i), { target: { value: 'Checkout crash on submit' } });
    fireEvent.change(within(dialog).getByLabelText(/priority/i), { target: { value: 'p1' } });
    fireEvent.change(within(dialog).getByLabelText(/tags/i), { target: { value: 'front-end, performance' } });
    fireEvent.click(within(dialog).getByRole('button', { name: /save metadata/i }));

    await waitFor(() => {
      const patchCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/bugs/bug-001/metadata') && options?.method === 'PATCH');
      expect(patchCall).toBeTruthy();
      expect(JSON.parse(patchCall[1].body)).toMatchObject({
        issueTitle: 'Checkout crash on submit',
        bugType: 'form_submission',
        projectId: 'proj-store',
        severity: 'high',
        priority: 'p1',
        tags: ['front-end', 'performance']
      });
    });
  });

  it('passes explicit server filter query params', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockResolvedValue({ ok: true, json: async () => [] });

    render(<ViewTicketsPage token="senior-token" userRole="senior" />);
    const user = userEvent.setup();

    await screen.findByText(/no active tickets/i);
    expect(screen.getByLabelText(/search tickets/i)).toBeInTheDocument();
    const filterToggle = screen.getByRole('button', { name: /show filters/i });
    expect(filterToggle).toHaveAttribute('aria-expanded', 'false');
    await user.click(filterToggle);
    expect(filterToggle).toHaveAttribute('aria-expanded', 'true');
    await user.selectOptions(screen.getByLabelText(/priority/i), 'p0');
    await user.selectOptions(screen.getByLabelText(/severity/i), 'urgent');
    await user.type(screen.getByLabelText(/^tag$/i), 'front-end');
    await user.type(screen.getByLabelText(/project id/i), 'proj-web');
    await user.type(screen.getByLabelText(/assignee id/i), 'usr_dev_001');
    await user.click(screen.getByRole('button', { name: /apply filters/i }));

    await waitFor(() => expect(screen.getByRole('button', { name: /hide filters \(5 active\)/i })).toBeInTheDocument());

    await waitFor(() => {
      const filteredCall = fetchSpy.mock.calls
        .map(([url]) => new URL(String(url), window.location.origin))
        .find((url) => url.searchParams.get('priority') === 'p0');
      expect(filteredCall.searchParams.get('severity')).toBe('urgent');
      expect(filteredCall.searchParams.get('tag')).toBe('front-end');
      expect(filteredCall.searchParams.get('projectId')).toBe('proj-web');
      expect(filteredCall.searchParams.get('assigneeUserId')).toBe('usr_dev_001');
    });
  });

  it('bulk assigns only IDs visible after quick filters', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
      const target = String(url);
      const method = options.method || 'GET';

      if (target.includes('/api/bugs?status=active') && method === 'GET') {
        return {
          ok: true,
          json: async () => [
            activeTicket({ id: 'urgent-visible', issueTitle: 'Payment outage', severity: 'urgent', priority: 'p0' }),
            activeTicket({ id: 'normal-hidden', issueTitle: 'Copy typo', severity: 'low', priority: 'p3' })
          ]
        };
      }

      if (target.endsWith('/api/bugs/assignees') && method === 'GET') {
        return { ok: true, json: async () => [{ userId: 'usr_dev_007', username: 'target-dev', email: 'target@example.com', role: 'dev', userType: 'human' }] };
      }

      if (target.endsWith('/api/bugs/bulk-allocate') && method === 'PATCH') {
        return { ok: true, json: async () => ({ updated: 1 }) };
      }

      throw new Error(`Unhandled fetch: ${method} ${target}`);
    });

    render(<ViewTicketsPage token="admin-token" userRole="admin" />);
    const user = userEvent.setup();

    await screen.findByText(/payment outage/i);
    await user.click(screen.getByRole('button', { name: /show filters/i }));
    await user.click(screen.getByRole('button', { name: /urgent/i }));
    expect(screen.queryByText(/copy typo/i)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /bulk assign visible tickets/i }));
    expect(await screen.findByRole('option', { name: 'target-dev - (target@example.com)' })).toBeInTheDocument();
    await user.selectOptions(await screen.findByLabelText(/assign visible tickets to/i), 'usr_dev_007');
    await user.click(screen.getByRole('button', { name: /assign 1 visible tickets/i }));

    await waitFor(() => {
      const bulkCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/bugs/bulk-allocate') && options?.method === 'PATCH');
      expect(bulkCall).toBeTruthy();
      expect(JSON.parse(bulkCall[1].body)).toEqual({ items: [{ ticketId: 'urgent-visible', expectedVersion: 7 }], assigneeUserId: 'usr_dev_007' });
    });
  });

  it('applies partial bulk successes immediately and retries only refreshed failures', async () => {
    let activeLoads = 0;
    let bulkCalls = 0;
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
      const target = String(url);
      const method = options.method || 'GET';

      if (target.includes('/api/bugs?status=active') && method === 'GET') {
        activeLoads += 1;
        return {
          ok: true,
          json: async () => activeLoads === 1 ? [
            activeTicket({ id: 'bulk-success', issueTitle: 'Assigned immediately', version: 4 }),
            activeTicket({ id: 'bulk-conflict', issueTitle: 'Needs conflict review', version: 5 }),
            activeTicket({ id: 'bulk-failed', issueTitle: 'Temporary assignment failure', version: 6 })
          ] : []
        };
      }

      if (target.endsWith('/api/bugs/assignees') && method === 'GET') {
        return { ok: true, json: async () => [{ userId: 'usr_dev_007', username: 'target-dev', email: 'target@example.com', role: 'dev', userType: 'human' }] };
      }

      if (target.endsWith('/api/bugs/bulk-conflict') && method === 'GET') {
        return { ok: true, json: async () => activeTicket({ id: 'bulk-conflict', issueTitle: 'Needs conflict review', status: 'reopened', version: 8 }) };
      }

      if (target.endsWith('/api/bugs/bulk-failed') && method === 'GET') {
        return { ok: true, json: async () => activeTicket({ id: 'bulk-failed', issueTitle: 'Temporary assignment failure', version: 9 }) };
      }

      if (target.endsWith('/api/bugs/bulk-allocate') && method === 'PATCH') {
        bulkCalls += 1;
        return bulkCalls === 1 ? {
          ok: true,
          json: async () => ({
            updated: [activeTicket({ id: 'bulk-success', issueTitle: 'Assigned immediately', version: 5, assigneeUserId: 'usr_dev_007' })],
            failed: [
              {
                ticketId: 'bulk-conflict',
                error: 'ticket_version_conflict',
                conflict: { ticketId: 'bulk-conflict', currentVersion: 8, currentStatus: 'reopened', changedFields: ['status'] }
              },
              { ticketId: 'bulk-failed', error: 'temporary_failure' }
            ]
          })
        } : {
          ok: true,
          json: async () => ({
            updated: [
              activeTicket({ id: 'bulk-conflict', version: 9 }),
              activeTicket({ id: 'bulk-failed', version: 10 })
            ],
            failed: []
          })
        };
      }

      throw new Error(`Unhandled fetch: ${method} ${target}`);
    });

    render(<ViewTicketsPage token="admin-token" userRole="admin" />);
    const user = userEvent.setup();
    await screen.findByText('Assigned immediately');
    await user.click(screen.getByRole('button', { name: /show filters/i }));
    await user.click(screen.getByRole('button', { name: /bulk assign visible tickets/i }));
    await user.selectOptions(await screen.findByLabelText(/assign visible tickets to/i), 'usr_dev_007');
    await user.click(screen.getByRole('button', { name: /assign 3 visible tickets/i }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /retry 2 failed tickets/i })).toBeDisabled();
    });
    expect(screen.getByText('Assigned immediately')).toBeInTheDocument();
    expect(screen.getByText(/^IDs:/i)).toHaveTextContent('bulk-conflict, bulk-failed');
    expect(screen.getByText(/^IDs:/i)).not.toHaveTextContent('bulk-success');
    expect(screen.getByText(/bulk-conflict: version 8/i)).toBeInTheDocument();
    expect(screen.getByText(/bulk-failed: temporary_failure/i)).toBeInTheDocument();

    const retryButton = screen.getByRole('button', { name: /retry 2 failed tickets/i });
    fireEvent.focus(screen.getByLabelText(/assign visible tickets to/i));
    expect(screen.getByRole('button', { name: /mark reviewed for bulk-conflict: status/i })).toBeInTheDocument();
    expect(retryButton).toBeDisabled();
    await user.click(screen.getByRole('button', { name: /mark reviewed for bulk-conflict: status/i }));
    await user.click(screen.getByRole('button', { name: /reconfirm bulk assignment/i }));
    expect(retryButton).toBeEnabled();
    await user.click(retryButton);

    await waitFor(() => expect(bulkCalls).toBe(2));
    const bodies = fetchSpy.mock.calls
      .filter(([url, options]) => String(url).endsWith('/api/bugs/bulk-allocate') && options?.method === 'PATCH')
      .map(([, options]) => JSON.parse(options.body));
    expect(bodies[0].items.map((item) => item.ticketId)).toEqual(['bulk-success', 'bulk-conflict', 'bulk-failed']);
    expect(bodies[1]).toEqual({
      items: [
        { ticketId: 'bulk-conflict', expectedVersion: 8 },
        { ticketId: 'bulk-failed', expectedVersion: 9 }
      ],
      assigneeUserId: 'usr_dev_007'
    });
  });

  it('validates reopen reason and removes reopened archived ticket', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
      const target = String(url);
      const method = options.method || 'GET';

      if (target.includes('/api/bugs?status=closed') && method === 'GET') {
        return {
          ok: true,
          json: async () => [{
            id: 'archived-001',
            issueTitle: 'Webhook failed',
            reporterUserId: 'qa@example.com',
            assigneeUserId: 'usr_dev_001',
            closeDate: '2026-01-01 12:45:00',
            projectName: 'Payments',
            severity: 'mid',
            priority: 'p2',
            version: 4
          }]
        };
      }

      if (target.endsWith('/api/bugs/archived-001/reopen') && method === 'PATCH') {
        return { ok: true, json: async () => ({ id: 'archived-001', status: 'reopened' }) };
      }

      throw new Error(`Unhandled fetch: ${method} ${target}`);
    });

    render(<ArchivedPage token="admin-token" userRole="admin" />);
    const user = userEvent.setup();

    fireEvent.click(await screen.findByText(/webhook failed/i));
    await user.click(screen.getByRole('menuitem', { name: /reopen/i }));
    await user.click(screen.getByRole('button', { name: /reopen ticket/i }));
    expect(screen.getByRole('alert')).toHaveTextContent(/reopen reason is required/i);

    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: 'Issue is still reproducible in production.' } });
    await user.click(screen.getByRole('button', { name: /reopen ticket/i }));

    await waitFor(() => {
      const reopenCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/bugs/archived-001/reopen') && options?.method === 'PATCH');
      expect(reopenCall).toBeTruthy();
      expect(JSON.parse(reopenCall[1].body)).toEqual({ reason: 'Issue is still reproducible in production.', expectedVersion: 4 });
      expect(screen.queryByText(/webhook failed/i)).not.toBeInTheDocument();
    });
  });

  it('shows unread notifications and marks them read when endpoint is available', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
      const target = String(url);
      const method = options.method || 'GET';

      if (target.includes('/api/notifications?unreadOnly=true') && method === 'GET') {
        return { ok: true, json: async () => [{ id: 'note-001', message: 'Ticket bug-001 was assigned to you.' }] };
      }

      if (target.endsWith('/api/notifications/note-001/read') && method === 'PATCH') {
        return { ok: true, status: 204, json: async () => ({}) };
      }

      throw new Error(`Unhandled fetch: ${method} ${target}`);
    });

    render(<NavBar currentPage="dashboard" onNavigate={() => {}} userRole="dev" token="token123" />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: /notifications, 1 unread/i }));
    expect(screen.getByText(/was assigned to you/i)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /mark read/i }));

    await waitFor(() => {
      expect(fetchSpy.mock.calls.some(([url, options]) => String(url).endsWith('/api/notifications/note-001/read') && options?.method === 'PATCH')).toBe(true);
      expect(screen.getByRole('button', { name: /notifications, 0 unread/i })).toBeInTheDocument();
    });
  });

  it('marks all unread notifications read from the menu', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
      const target = String(url);
      const method = options.method || 'GET';

      if (target.includes('/api/notifications?unreadOnly=true') && method === 'GET') {
        return {
          ok: true,
          json: async () => [
            { id: 'note-001', message: 'Ticket bug-001 was assigned to you.' },
            { id: 'note-002', message: 'Ticket bug-002 has a new comment.' }
          ]
        };
      }

      if (target.endsWith('/api/notifications/read-all') && method === 'PATCH') {
        return { ok: true, status: 200, json: async () => ({ updated: 2 }) };
      }

      throw new Error(`Unhandled fetch: ${method} ${target}`);
    });

    render(<NavBar currentPage="dashboard" onNavigate={() => {}} userRole="dev" token="token123" />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: /notifications, 2 unread/i }));
    await user.click(screen.getByText(/mark all read/i));

    await waitFor(() => {
      expect(fetchSpy.mock.calls.some(([url, options]) => String(url).endsWith('/api/notifications/read-all') && options?.method === 'PATCH')).toBe(true);
      expect(screen.getByRole('button', { name: /notifications, 0 unread/i })).toBeInTheDocument();
    });
  });
});
