import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '../../react/node_modules/@testing-library/react/dist/index.js';
import userEvent from '../../react/node_modules/@testing-library/user-event/dist/esm/index.js';
import App from '../../react/src/App';

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
  document.querySelector('meta[name="bug-tracker-demo"]')?.remove();
  window.history.replaceState({}, '', '/');
});

describe('login page', () => {
  it('shows validation error when form is submitted empty', async () => {
    render(<App />);

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(screen.getByRole('alert')).toHaveTextContent('Email and password are required.');
  });

  it('signs in and shows session card on success', async () => {
    vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          accessToken: 'token123',
          user: { userId: 'usr_dev_001', email: 'dev@example.com', role: 'dev' }
        })
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => []
      });

    render(<App />);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/email/i), 'dev@example.com');
    await user.type(screen.getByLabelText(/password/i), 'DevPass123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByTestId('session-card')).toBeInTheDocument();
    expect(screen.getByText('usr_dev_001')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: /dashboard/i })).toBeInTheDocument();
    expect(localStorage.getItem('bug_tracker_access_token')).toBe('token123');
  });

  it('revokes the server token and clears the local session on logout', async () => {
    const fetchMock = vi.spyOn(global, 'fetch').mockImplementation(async (url) => {
      if (String(url).endsWith('/api/auth/login')) {
        return {
          ok: true,
          json: async () => ({
            accessToken: 'logout-token',
            user: { userId: 'usr_dev_001', email: 'dev@example.com', role: 'dev' }
          })
        };
      }

      if (String(url).endsWith('/api/auth/logout')) {
        return { ok: true, status: 204 };
      }

      return { ok: true, json: async () => [] };
    });

    render(<App />);
    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/email/i), 'dev@example.com');
    await user.type(screen.getByLabelText(/password/i), 'DevPass123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));
    await screen.findByTestId('session-card');

    await user.click(screen.getByRole('button', { name: /log out/i }));

    expect(await screen.findByRole('heading', { level: 1, name: /sign in/i })).toBeInTheDocument();
    expect(localStorage.getItem('bug_tracker_access_token')).toBeNull();
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringMatching(/\/api\/auth\/logout$/),
      expect.objectContaining({ headers: { Authorization: 'Bearer logout-token' } })
    );
  });

  it('shows the disposable-data warning and autofills a selected demo role', async () => {
    const meta = document.createElement('meta');
    meta.name = 'bug-tracker-demo';
    meta.content = btoa(JSON.stringify({
      resetAtUtc: '04:00',
      accounts: [
        { role: 'Senior', email: 'alex.senior@example.com', password: 'SeniorPass123!', description: 'Triage and assign tickets.' }
      ]
    }));
    document.head.append(meta);

    render(<App />);
    expect(screen.getByText(/all data is synthetic, public, and mutable/i)).toBeInTheDocument();
    expect(screen.getByText(/do not enter personal, private, or confidential/i)).toBeInTheDocument();
    expect(screen.getByText(/use existing premade accounts or submit a request/i)).toBeInTheDocument();
    expect(screen.queryByText(/five-minute walkthrough/i)).not.toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /senior/i }));
    expect(screen.getByLabelText(/email/i)).toHaveValue('alex.senior@example.com');
    expect(screen.getByLabelText(/password/i)).toHaveValue('SeniorPass123!');
  });

  it('explains that demo access requests use fictional addresses and do not send email', async () => {
    const meta = document.createElement('meta');
    meta.name = 'bug-tracker-demo';
    meta.content = btoa(JSON.stringify({ resetAtUtc: '04:00', accounts: [] }));
    document.head.append(meta);

    render(<App />);
    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /request access/i }));

    expect(screen.getByText(/use a fictitious email address/i)).toBeInTheDocument();
    expect(screen.getByText(/no email is sent/i)).toBeInTheDocument();
    expect(screen.getByText(/sign in with the admin demo account/i)).toBeInTheDocument();
    expect(screen.getByText(/open users, then requests/i)).toBeInTheDocument();
  });

  it('requires a six-character password with a number and special character during setup', async () => {
    window.history.replaceState({}, '', '/setup-password?email=new.user%40example.com&token=setup-token');
    render(<App />);
    const user = userEvent.setup();

    await user.type(await screen.findByLabelText(/^new password$/i), 'abcdef');
    await user.type(screen.getByLabelText(/confirm new password/i), 'abcdef');
    await user.click(screen.getByRole('button', { name: /set password/i }));

    expect(screen.getByRole('alert')).toHaveTextContent('Password must be at least 6 characters with one number and one special character.');
    expect(screen.getByText(/use at least 6 characters, including a number and special character/i)).toBeInTheDocument();
  });

  it('locks the account email on a password link', async () => {
    window.history.replaceState({}, '', '/setup-password?email=new.user%40example.com&token=setup-token');
    render(<App />);

    expect(await screen.findByLabelText(/^email$/i)).toHaveValue('new.user@example.com');
    expect(screen.getByLabelText(/^email$/i)).toHaveAttribute('readonly');
    expect(screen.getByLabelText(/confirm email/i)).toHaveAttribute('readonly');
    expect(screen.getByText(/password link is issued for this email address/i)).toBeInTheDocument();
  });

  it('submits an AI credential recovery request from the sign-in screen', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockResolvedValueOnce({
      ok: true,
      json: async () => ({ message: 'If the account exists, an administrator can review the oath-token recovery request.' })
    });
    render(<App />);
    const user = userEvent.setup();

    await user.click(screen.getByRole('button', { name: /forgot password/i }));
    await user.click(screen.getByRole('combobox', { name: /account type/i }));
    await user.click(screen.getByRole('option', { name: /ai agent oath token/i }));
    await user.type(screen.getByLabelText(/^email$/i), 'agent@example.com');
    await user.type(screen.getByLabelText(/confirm email/i), 'agent@example.com');
    await user.click(screen.getByRole('button', { name: /request recovery/i }));

    expect(await screen.findByText(/oath-token recovery request/i)).toBeInTheDocument();
    expect(fetchSpy).toHaveBeenCalledWith(
      expect.stringMatching(/\/api\/auth\/request-credential-recovery$/),
      expect.objectContaining({ body: JSON.stringify({ email: 'agent@example.com', requestType: 'ai_agent' }) })
    );
  });
});
