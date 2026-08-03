import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '../../react/node_modules/@testing-library/react/dist/index.js';
import userEvent from '../../react/node_modules/@testing-library/user-event/dist/esm/index.js';
import App from '../../react/src/App';

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.clear();
});

describe('role login coverage', () => {
  const cases = [
    { role: 'dev', email: 'dev@example.com', userId: 'usr_dev_001' },
    { role: 'senior', email: 'senior@example.com', userId: 'usr_senior_001' },
    { role: 'admin', email: 'admin@example.com', userId: 'usr_admin_001' }
  ];

  it.each(cases)('logs in and displays dashboard identity for $role', async ({ role, email, userId }) => {
    vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          accessToken: `${role}-token`,
          user: { userId, email, role }
        })
      })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => []
      });

    render(<App />);

    const user = userEvent.setup();
    await user.type(screen.getByLabelText(/email/i), email);
    await user.type(screen.getByLabelText(/password/i), 'ValidPass123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByTestId('session-card')).toBeInTheDocument();
    expect(screen.getByText(userId)).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: /dashboard/i })).toBeInTheDocument();
    expect(localStorage.getItem('bug_tracker_access_token')).toBe(`${role}-token`);
  });
});
