import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '../../react/node_modules/@testing-library/react/dist/index.js';
import userEvent from '../../react/node_modules/@testing-library/user-event/dist/esm/index.js';
import { clearBugCache } from '../../react/src/api/bugs';
import ViewTicketsPage from '../../react/src/pages/ViewTicketsPage';

afterEach(() => {
  vi.restoreAllMocks();
  clearBugCache();
});

function mockDownloadApis() {
  if (!URL.createObjectURL) {
    URL.createObjectURL = () => 'blob:export';
  }
  if (!URL.revokeObjectURL) {
    URL.revokeObjectURL = () => {};
  }
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:export');
  vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
}

describe('ticket export controls', () => {
  it('shows export controls for senior users and hides them for dev users', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => [
        {
          id: 'bug-001',
          issueTitle: 'Visible export bug',
          status: 'todo',
          reporterUserId: 'qa@example.com',
          createdAt: '2026-01-01 09:00:00',
          projectName: 'Storefront',
          severity: 'mid',
          priority: 'p2'
        }
      ]
    });

    const { unmount } = render(<ViewTicketsPage token="senior-token" userRole="senior" />);
    expect(await screen.findByText(/export visible/i)).toBeInTheDocument();
    unmount();
    clearBugCache();

    render(<ViewTicketsPage token="dev-token" userRole="dev" />);
    await screen.findByText(/visible export bug/i);
    expect(screen.queryByText(/export visible/i)).not.toBeInTheDocument();
  });

  it('exports the ticket IDs visible after quick filters', async () => {
    mockDownloadApis();
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
      const target = String(url);
      const method = options.method || 'GET';

      if (target.includes('/api/bugs?status=active') && method === 'GET') {
        return {
          ok: true,
          json: async () => [
            {
              id: 'urgent-visible',
              issueTitle: 'Payment outage',
              status: 'todo',
              reporterUserId: 'qa@example.com',
              createdAt: '2026-01-01 09:00:00',
              projectName: 'Payments',
              severity: 'urgent',
              priority: 'p0'
            },
            {
              id: 'normal-hidden',
              issueTitle: 'Copy typo',
              status: 'todo',
              reporterUserId: 'qa@example.com',
              createdAt: '2026-01-02 09:00:00',
              projectName: 'Marketing',
              severity: 'low',
              priority: 'p3'
            }
          ]
        };
      }

      if (target.endsWith('/api/bugs/export') && method === 'POST') {
        return {
          ok: true,
          headers: { get: () => 'attachment; filename="bugs.json"' },
          blob: async () => new Blob(['[]'], { type: 'application/json' })
        };
      }

      throw new Error(`Unhandled fetch: ${method} ${target}`);
    });

    render(<ViewTicketsPage token="admin-token" userRole="admin" />);
    const user = userEvent.setup();

    await screen.findByText(/payment outage/i);
    await user.click(screen.getByRole('button', { name: /show filters/i }));
    await user.click(screen.getByRole('button', { name: /urgent/i }));
    expect(screen.queryByText(/copy typo/i)).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /^json$/i }));

    await waitFor(() => {
      const exportCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/bugs/export') && options?.method === 'POST');
      expect(exportCall).toBeTruthy();
      expect(JSON.parse(exportCall[1].body)).toEqual({
        format: 'json',
        ticketIds: ['urgent-visible']
      });
    });
  });
});
