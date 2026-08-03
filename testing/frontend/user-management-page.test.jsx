import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '../../react/node_modules/@testing-library/react/dist/index.js';
import userEvent from '../../react/node_modules/@testing-library/user-event/dist/esm/index.js';
import UserManagementPage from '../../react/src/pages/UserManagementPage';

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('user request management', () => {
  it('lets an admin update an active user username while preserving the user ID', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ userId: 'usr_dev_001', username: 'dev-user', email: 'dev@example.com', role: 'dev', userType: 'human', isActive: 1 }]
      })
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ userId: 'usr_dev_001', username: 'frontend-dev', email: 'dev@example.com', role: 'dev', userType: 'human', isActive: 1 })
      });

    render(<UserManagementPage token="admin-token" />);
    const user = userEvent.setup();

    const usernameCell = await screen.findByText('dev-user');
    fireEvent.click(usernameCell);
    await user.click(screen.getByRole('menuitem', { name: /^edit username$/i }));
    const input = screen.getByRole('textbox', { name: /^username$/i });
    expect(input).toHaveValue('dev-user');
    expect(screen.getByText(/email remains the login/i)).toBeInTheDocument();
    await user.clear(input);
    await user.type(input, 'Frontend-Dev');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    expect(await screen.findByText('frontend-dev')).toBeInTheDocument();
    const patchCall = fetchSpy.mock.calls.find(([url, options]) => String(url).endsWith('/api/auth/users/usr_dev_001/username') && options?.method === 'PATCH');
    expect(JSON.parse(patchCall[1].body)).toEqual({ username: 'Frontend-Dev' });
  });

  it('shows human last-online and ai agent websocket presence statuses', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(new Date('2026-04-01T12:00:00Z'));

    vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [
          { userId: 'usr_active', email: 'active@example.com', role: 'dev', userType: 'human', isActive: 1, presenceStatus: 'active', isOnline: true, lastSeenAt: '2026-04-01 11:59:00' },
          { userId: 'usr_away', email: 'away@example.com', role: 'dev', userType: 'human', isActive: 1, presenceStatus: 'last_online', isOnline: false, lastSeenAt: '2026-04-01 09:00:00' },
          { userId: 'usr_agent_connected', email: 'agent@example.com', role: 'dev', userType: 'agent', isActive: 1, presenceStatus: 'connected', isOnline: true },
          { userId: 'usr_agent_offline', email: 'agent2@example.com', role: 'dev', userType: 'agent', isActive: 1, presenceStatus: 'offline', isOnline: false }
        ]
      })
      .mockResolvedValueOnce({ ok: true, json: async () => [] });

    render(<UserManagementPage token="admin-token" />);

    expect(await screen.findByText('Active')).toBeInTheDocument();
    expect(screen.getByText('Last online 3 hours ago')).toBeInTheDocument();
    expect(screen.getByText('Connected')).toBeInTheDocument();
    expect(screen.getByText('Offline')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: /last active/i })).toBeInTheDocument();
    expect(screen.getByText('3 hours ago')).toBeInTheDocument();
    expect(screen.getByText(/click or tap a user for options/i)).toBeInTheDocument();
  });

  it('opens human request context menu and updates username', async () => {
    vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ userId: 'usr_dev_001', email: 'dev@example.com', role: 'dev', userType: 'human', isActive: 1 }]
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{
          requestId: 'req_1',
          requestType: 'human',
          email: 'dev@example.com',
          username: 'usr_dev_001',
          status: 'pending',
          createdAt: '2026-03-01 09:00:00'
        }]
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          requestId: 'req_1',
          requestType: 'human',
          email: 'dev@example.com',
          username: 'usr_dev_renamed',
          status: 'pending',
          createdAt: '2026-03-01 09:00:00'
        })
      });

    render(<UserManagementPage token="admin-token" />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('tab', { name: /requests/i }));
    const rowCell = await screen.findByText('usr_dev_001');
    fireEvent.click(rowCell);
    await user.click(screen.getByRole('menuitem', { name: /edit username/i }));
    await user.clear(screen.getByRole('textbox', { name: /^username$/i }));
    await user.type(screen.getByRole('textbox', { name: /^username$/i }), 'usr_dev_renamed');
    await user.click(screen.getByRole('button', { name: /^save$/i }));

    expect(await screen.findByText(/username updated/i)).toBeInTheDocument();
  });

  it('creates a new ai agent request from email form', async () => {
    vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          requestId: 'req_2',
          requestType: 'ai_agent',
          email: 'agent@example.com',
          username: 'usr_agent_100',
          status: 'pending',
          createdAt: '2026-03-01 09:30:00'
        })
      });

    render(<UserManagementPage token="admin-token" />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('tab', { name: /requests/i }));
    await user.click(await screen.findByRole('combobox', { name: /^type$/i }));
    await user.click(screen.getByRole('option', { name: /ai agent/i }));
    await user.type(screen.getByLabelText(/^email$/i), 'agent@example.com');
    await user.type(screen.getByLabelText(/confirm email/i), 'agent@example.com');
    await user.click(screen.getByRole('button', { name: /create request/i }));

    expect(await screen.findByText(/request created/i)).toBeInTheDocument();
    expect(screen.getByText('agent@example.com')).toBeInTheDocument();
  });

  it('generates and displays an oath token for an ai agent request', async () => {
    vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ userId: 'usr_agent_300', email: 'robot@example.com', role: 'dev', userType: 'agent', isActive: 1 }]
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{
          requestId: 'req_3',
          requestType: 'ai_agent',
          email: 'robot@example.com',
          username: 'usr_agent_300',
          status: 'pending',
          createdAt: '2026-03-01 10:00:00'
        }]
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          message: 'api key issued',
          apiKey: 'ai-key-1234567890',
          username: 'usr_agent_300',
          expiresAt: '2026-03-31T10:00:00Z'
        })
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ userId: 'usr_agent_300', email: 'robot@example.com', role: 'dev', userType: 'agent', isActive: 1 }]
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{
          requestId: 'req_3',
          requestType: 'ai_agent',
          email: 'robot@example.com',
          username: 'usr_agent_300',
          userId: 'usr_agent_300',
          apiKeyPrefix: 'ai-key-123456789',
          apiKeyExpiresAt: '2026-03-31 10:00:00',
          status: 'approved',
          createdAt: '2026-03-01 10:00:00'
        }]
      });

    render(<UserManagementPage token="admin-token" />);
    const user = userEvent.setup();

    await user.click(await screen.findByRole('tab', { name: /requests/i }));
    const rowCell = await screen.findByText('usr_agent_300');
    fireEvent.click(rowCell);
    await user.click(screen.getByRole('menuitem', { name: /generate oath token/i }));
    expect(screen.getByRole('dialog', { name: /generate ai oath token/i })).toBeInTheDocument();
    await user.clear(screen.getByRole('spinbutton', { name: /active days/i }));
    await user.type(screen.getByRole('spinbutton', { name: /active days/i }), '30');
    await user.click(screen.getByRole('button', { name: /generate token/i }));

    expect(await screen.findByDisplayValue('ai-key-1234567890')).toBeInTheDocument();
    expect(screen.getByDisplayValue('usr_agent_300')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /copy token/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /email token/i })).toHaveAttribute('href', expect.stringContaining('mailto:'));
    expect(screen.getByText(/add it to the required projects in project management/i)).toBeInTheDocument();
    expect(screen.getByText(/\/api\/agent\/notifications\/ws/i)).toBeInTheDocument();
  });

  it('reissues an oath token from the ai agent user row menu', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ userId: 'usr_agent_300', username: 'release-bot', email: 'robot@example.com', role: 'dev', userType: 'agent', isActive: 1, presenceStatus: 'offline', isOnline: false }]
      })
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          message: 'oath token issued',
          apiKey: 'fresh-oath-token-123',
          username: 'usr_agent_300',
          expiresAt: '2026-04-14T10:00:00Z'
        })
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ userId: 'usr_agent_300', username: 'release-bot', email: 'robot@example.com', role: 'dev', userType: 'agent', isActive: 1, presenceStatus: 'offline', isOnline: false }]
      })
      .mockResolvedValueOnce({ ok: true, json: async () => [] });

    render(<UserManagementPage token="admin-token" />);
    const user = userEvent.setup();

    const rowCell = await screen.findByText('release-bot');
    fireEvent.click(rowCell);
    await user.click(screen.getByRole('menuitem', { name: /reissue oath token/i }));
    expect(screen.getByRole('dialog', { name: /generate ai oath token/i })).toBeInTheDocument();
    await user.clear(screen.getByRole('spinbutton', { name: /active days/i }));
    await user.type(screen.getByRole('spinbutton', { name: /active days/i }), '14');
    await user.click(screen.getByRole('button', { name: /generate token/i }));

    expect(await screen.findByDisplayValue('fresh-oath-token-123')).toBeInTheDocument();
    expect(fetchSpy.mock.calls.some(([url, options]) => String(url).endsWith('/api/auth/users/usr_agent_300/issue-api-key') && options?.method === 'POST' && JSON.parse(options.body).activeDays === 14)).toBe(true);
    expect(await screen.findByText(/oath token reissued/i)).toBeInTheDocument();
  });

  it('shows an in-app copyable password link for a human recovery request', async () => {
    const recoveryRequest = {
      requestId: 'recovery_reset_1', requestType: 'human', purpose: 'credential_recovery', email: 'dev@example.com',
      username: 'dev-user', userId: 'usr_dev_001', status: 'pending', createdAt: '2026-03-01 10:00:00'
    };
    vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({ ok: true, json: async () => [recoveryRequest] })
      .mockResolvedValueOnce({ ok: true, json: async () => ({ link: 'https://demo.example/setup-password?token=reset-token', expiresAt: '2026-03-01T10:30:00Z' }) })
      .mockResolvedValueOnce({ ok: true, json: async () => [] })
      .mockResolvedValueOnce({ ok: true, json: async () => [recoveryRequest] });

    render(<UserManagementPage token="admin-token" />);
    const user = userEvent.setup();
    await user.click(await screen.findByRole('tab', { name: /requests/i }));
    fireEvent.click(await screen.findByText('dev-user'));
    await user.click(screen.getByRole('menuitem', { name: /issue password reset link/i }));

    expect(await screen.findByRole('dialog', { name: /password link ready/i })).toBeInTheDocument();
    expect(screen.getByDisplayValue('https://demo.example/setup-password?token=reset-token')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /copy link/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /email link/i })).toHaveAttribute('href', expect.stringContaining('mailto:dev%40example.com'));
  });
});
