import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  createSessionManager,
  initializeSessionActivity,
  SESSION_ACTIVITY_KEY,
  SESSION_TOKEN_KEY
} from '../../react/src/session_manager';

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date('2026-07-30T12:00:00Z'));
  localStorage.clear();
  localStorage.setItem(SESSION_TOKEN_KEY, 'token123');
  initializeSessionActivity();
});

afterEach(() => {
  vi.useRealTimers();
  localStorage.clear();
});

describe('session manager', () => {
  it('ends a session after the configured inactivity period', () => {
    const onEnd = vi.fn();
    const manager = createSessionManager({ onEnd, inactivityTimeoutMs: 45 * 60 * 1000 });
    manager.start();

    vi.advanceTimersByTime(45 * 60 * 1000);

    expect(onEnd).toHaveBeenCalledWith({ reason: 'inactive', token: 'token123' });
  });

  it('extends the shared deadline after user activity', () => {
    const onEnd = vi.fn();
    const manager = createSessionManager({ onEnd, inactivityTimeoutMs: 45 * 60 * 1000 });
    manager.start();

    vi.advanceTimersByTime(30 * 60 * 1000);
    window.dispatchEvent(new Event('pointerdown'));
    vi.advanceTimersByTime(30 * 60 * 1000);
    expect(onEnd).not.toHaveBeenCalled();

    vi.advanceTimersByTime(15 * 60 * 1000);
    expect(onEnd).toHaveBeenCalledOnce();
  });

  it('uses activity from another tab to reschedule expiration', () => {
    const onEnd = vi.fn();
    const manager = createSessionManager({ onEnd, inactivityTimeoutMs: 45 * 60 * 1000 });
    manager.start();

    vi.advanceTimersByTime(30 * 60 * 1000);
    const oldValue = localStorage.getItem(SESSION_ACTIVITY_KEY);
    const newValue = String(Date.now());
    localStorage.setItem(SESSION_ACTIVITY_KEY, newValue);
    window.dispatchEvent(new StorageEvent('storage', { key: SESSION_ACTIVITY_KEY, oldValue, newValue }));
    vi.advanceTimersByTime(30 * 60 * 1000);

    expect(onEnd).not.toHaveBeenCalled();
  });

  it('ends the local session when another tab removes its token', () => {
    const onEnd = vi.fn();
    const manager = createSessionManager({ onEnd });
    manager.start();

    localStorage.removeItem(SESSION_TOKEN_KEY);
    window.dispatchEvent(new StorageEvent('storage', {
      key: SESSION_TOKEN_KEY,
      oldValue: 'token123',
      newValue: null
    }));

    expect(onEnd).toHaveBeenCalledWith({ reason: 'external', token: null });
  });

  it('removes listeners and timers when stopped', () => {
    const onEnd = vi.fn();
    const manager = createSessionManager({ onEnd, inactivityTimeoutMs: 1000 });
    manager.start();
    manager.stop();

    vi.advanceTimersByTime(2000);
    window.dispatchEvent(new Event('pointerdown'));

    expect(onEnd).not.toHaveBeenCalled();
  });
});
