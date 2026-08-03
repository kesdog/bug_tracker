import { afterEach, describe, expect, it, vi } from 'vitest';
import { fetchMe, login, logout } from '../../react/src/api/auth';

afterEach(() => {
  vi.restoreAllMocks();
});

describe('auth client', () => {
  it('returns login payload when credentials are valid', async () => {
    const mockResponse = {
      accessToken: 'token123',
      user: { userId: 'usr_dev_001', email: 'dev@example.com', role: 'dev' }
    };

    vi.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => mockResponse
    });

    const result = await login('dev@example.com', 'DevPass123!');
    expect(result).toEqual(mockResponse);
  });

  it('throws friendly error for unauthorized login', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValue({ ok: false, status: 401 });

    await expect(login('dev@example.com', 'bad-pass')).rejects.toThrow(
      'Invalid email or password.'
    );
  });

  it('uses Retry-After guidance for rate-limited login', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValue({
      ok: false,
      status: 429,
      headers: { get: () => '600' }
    });

    await expect(login('dev@example.com', 'bad-pass')).rejects.toThrow(
      'Too many attempts. Try again in 10 minutes.'
    );
  });

  it('fetches profile for valid session token', async () => {
    const profile = { userId: 'usr_dev_001', email: 'dev@example.com', role: 'dev' };

    vi.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => profile
    });

    const result = await fetchMe('token123');
    expect(result).toEqual(profile);
  });

  it('signals the application when a protected request returns 401', async () => {
    const unauthorizedHandler = vi.fn();
    window.addEventListener('bug-tracker:session-unauthorized', unauthorizedHandler, { once: true });
    vi.spyOn(global, 'fetch').mockResolvedValue({ ok: false, status: 401 });

    await expect(fetchMe('expired-token')).rejects.toThrow('Session is invalid or expired.');

    expect(unauthorizedHandler).toHaveBeenCalledOnce();
  });

  it('revokes the active bearer token on logout', async () => {
    const fetchMock = vi.spyOn(global, 'fetch').mockResolvedValue({ ok: true, status: 204 });

    await logout('token123');

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringMatching(/\/api\/auth\/logout$/),
      {
        method: 'POST',
        headers: { Authorization: 'Bearer token123' }
      }
    );
  });
});
