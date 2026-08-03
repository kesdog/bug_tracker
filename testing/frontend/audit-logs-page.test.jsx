import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '../../react/node_modules/@testing-library/react/dist/index.js';
import userEvent from '../../react/node_modules/@testing-library/user-event/dist/esm/index.js';
import AuditLogsPage from '../../react/src/pages/AuditLogsPage';

afterEach(() => {
  vi.restoreAllMocks();
});

describe('AuditLogsPage', () => {
  it('searches and renders admin audit logs', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url) => {
      const target = String(url);
      const parsed = new URL(target, window.location.origin);
      const search = parsed.searchParams.get('search');

      return {
        ok: true,
        json: async () => search === 'checkout'
          ? [
              {
                id: 'log-002',
                occurredAt: '2026-07-13 10:00:00',
                actor: 'agent_checkout_bot',
                actorType: 'agent',
                action: 'ticket.updated',
                ticketId: 'bug-123',
                summary: 'Updated checkout repro steps.'
              }
            ]
          : []
      };
    });

    render(<AuditLogsPage token="admin-token" />);
    const user = userEvent.setup();

    expect(await screen.findByText(/no audit logs match/i)).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText(/actor type/i), 'agent');
    await user.type(screen.getByLabelText(/search logs/i), 'checkout');
    await user.type(screen.getByLabelText(/ticket id/i), 'bug-123');
    await user.type(screen.getByLabelText(/^action$/i), 'ticket.updated');
    await user.click(screen.getByRole('button', { name: /search logs/i }));

    expect(await screen.findByText(/agent_checkout_bot/i)).toBeInTheDocument();
    expect(screen.getByText(/updated checkout repro steps/i)).toBeInTheDocument();

    await waitFor(() => {
      const latestCall = fetchSpy.mock.calls.at(-1);
      const url = new URL(String(latestCall[0]), window.location.origin);
      expect(url.pathname).toBe('/api/audit-logs');
      expect(url.searchParams.get('actorType')).toBe('agent');
      expect(url.searchParams.get('search')).toBe('checkout');
      expect(url.searchParams.get('ticketId')).toBe('bug-123');
      expect(url.searchParams.get('action')).toBe('ticket.updated');
    });
  });

  it('offers system lifecycle logs as an actor filter', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValue({ ok: true, json: async () => [] });
    render(<AuditLogsPage token="admin-token" />);

    expect(await screen.findByRole('option', { name: 'System' })).toBeInTheDocument();
  });
});
