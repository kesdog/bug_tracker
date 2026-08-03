import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '../../react/node_modules/@testing-library/react/dist/index.js';
import { clearBugCache } from '../../react/src/api/bugs';
import AllocatedPage from '../../react/src/pages/AllocatedPage';
import ArchivedPage from '../../react/src/pages/ArchivedPage';
import SubmittedReportsPage from '../../react/src/pages/SubmittedReportsPage';
import ViewTicketsPage from '../../react/src/pages/ViewTicketsPage';

afterEach(() => {
  vi.restoreAllMocks();
  clearBugCache();
});

describe('allocated and archived report flows', () => {
  it('opens the submitted bug report editor from allocated bug options', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
      const target = String(url);
      const method = options.method || 'GET';

      if (target.includes('/api/bugs/allocated?') && method === 'GET') {
        return {
          ok: true,
          json: async () => [
            {
              id: 'allocated-001',
              issueTitle: 'Checkout crash',
              status: 'open',
              createdAt: '2026-01-01 09:00:00',
              projectName: 'Storefront',
              severity: 'high'
            }
          ]
        };
      }

      if (target.endsWith('/api/bugs/allocated-001') && method === 'GET') {
        return {
          ok: true,
          json: async () => ({
            id: 'allocated-001',
            issueTitle: 'Checkout crash',
            description: 'Original submitted report.',
            status: 'open',
            projectName: 'Storefront',
            reporterUserId: 'usr_qa_001',
            assigneeUserId: 'usr_dev_001',
            createdAt: '2026-01-01 09:00:00',
            assignedAt: '2026-01-01 09:30:00',
            severity: 'high',
            reportImages: [],
            resolutionReportImages: [],
            version: 2
          })
        };
      }

      if (target.endsWith('/api/bugs/allocated-001/initial-report') && method === 'PATCH') {
        return {
          ok: true,
          json: async () => ({ id: 'allocated-001', description: 'Edited submitted report.' })
        };
      }

      throw new Error(`Unhandled fetch: ${method} ${target}`);
    });

    render(<AllocatedPage token="token123" />);

    expect(await screen.findByLabelText(/search allocated bugs/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /options/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /view reports for checkout crash/i })).not.toBeInTheDocument();

    const filterToggle = screen.getByRole('button', { name: /show filters/i });
    expect(filterToggle).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(filterToggle);
    expect(filterToggle).toHaveAttribute('aria-expanded', 'true');

    fireEvent.click(await screen.findByText(/checkout crash/i));
    fireEvent.click(screen.getByRole('menuitem', { name: /edit bug report/i }));

    await screen.findByDisplayValue('Original submitted report.');
    expect(screen.getByRole('textbox', { name: /bug report text block 1/i })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: /bug report editor/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /add image below/i })).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: /save bug report/i }));

    await waitFor(() => {
      const patchCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/bugs/allocated-001/initial-report') && options?.method === 'PATCH');
      expect(patchCall).toBeTruthy();
      expect(JSON.parse(patchCall[1].body)).toEqual({
        reportText: 'Original submitted report.',
        reportImages: [],
        expectedVersion: 2
      });
    });
  }, 10000);

  it('keeps archived reports and reopen reachable from the row menu', async () => {
    vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
      const target = String(url);
      const method = options.method || 'GET';

      if (target.includes('/api/bugs?status=closed') && method === 'GET') {
        return {
          ok: true,
          json: async () => [
            {
              id: 'archived-001',
              issueTitle: 'Webhook failed',
              reporterUserId: 'usr_qa_001',
              closeDate: '2026-01-01 12:45:00',
              projectName: 'Payments',
              severity: 'mid',
              assigneeUserId: 'usr_dev_001',
              resolvedByUserId: 'usr_dev_001'
            }
          ]
        };
      }

      if (target.endsWith('/api/bugs/archived-001') && method === 'GET') {
        return {
          ok: true,
          json: async () => ({
            id: 'archived-001',
            issueTitle: 'Webhook failed',
            description: 'The callback failed.',
            postResolutionReport: 'Webhook validation fixed.',
            status: 'closed',
            closeDate: '2026-01-01 12:45:00',
            attachments: []
          })
        };
      }

      throw new Error(`Unhandled fetch: ${method} ${target}`);
    });

    render(<ArchivedPage token="token123" userRole="admin" />);

    expect(await screen.findByText(/webhook failed/i)).toBeInTheDocument();
    expect(screen.queryByRole('columnheader', { name: /actions/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /view reports for webhook failed/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^reopen$/i })).not.toBeInTheDocument();
    const filterToggle = screen.getByRole('button', { name: /show filters/i });
    expect(filterToggle).toHaveAttribute('aria-expanded', 'false');
    fireEvent.click(screen.getByText(/webhook failed/i));
    expect(screen.getByRole('menuitem', { name: /reopen/i })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('menuitem', { name: /view reports/i }));
    expect(await screen.findByText(/the callback failed/i)).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /solution/i })).toBeInTheDocument();
  });

  it.each([
    ['active', ViewTicketsPage, { userRole: 'dev' }, false],
    ['submitted', SubmittedReportsPage, { currentUserId: 'usr-reporter' }, false]
  ])('keeps %s reports reachable and fetches full detail', async (_name, Page, extraProps, hasVisibleButton) => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
      const target = String(url);
      const method = options.method || 'GET';
      if (target.includes('/api/bugs?status=active') && method === 'GET') {
        return { ok: true, json: async () => [{ id: 'visible-001', issueTitle: 'Visible report action', status: 'open', reporterUserId: 'usr-reporter' }] };
      }
      if (target.includes('/api/bugs?status=closed') && method === 'GET') {
        return { ok: true, json: async () => [] };
      }
      if (target.endsWith('/api/bugs/visible-001') && method === 'GET') {
        return { ok: true, json: async () => ({ id: 'visible-001', issueTitle: 'Visible report action', status: 'open', description: 'Full detail only' }) };
      }
      throw new Error(`Unhandled fetch: ${method} ${target}`);
    });

    render(<Page token="token123" {...extraProps} />);
    const ticketTitle = await screen.findByText(/visible report action/i);
    if (hasVisibleButton) {
      fireEvent.click(screen.getByRole('button', { name: /view reports for visible report action/i }));
    } else {
      expect(screen.queryByRole('columnheader', { name: /reports/i })).not.toBeInTheDocument();
      fireEvent.click(ticketTitle);
      expect(screen.queryByRole('menuitem', { name: /^contact$/i })).not.toBeInTheDocument();
      fireEvent.click(screen.getByRole('menuitem', { name: /view reports/i }));
    }
    expect(await screen.findByText('Full detail only')).toBeInTheDocument();
    expect(fetchSpy.mock.calls.some(([url]) => String(url).endsWith('/api/bugs/visible-001'))).toBe(true);
  });
});
