const DEFAULT_TIMEOUT_MINUTES = 45;
const ACTIVITY_WRITE_INTERVAL_MS = 15_000;

export const SESSION_TOKEN_KEY = 'bug_tracker_access_token';
export const SESSION_ACTIVITY_KEY = 'bug_tracker_last_activity';

const configuredTimeoutMinutes = Number(import.meta.env.VITE_SESSION_INACTIVITY_MINUTES);
export const SESSION_INACTIVITY_TIMEOUT_MS =
  Number.isFinite(configuredTimeoutMinutes) && configuredTimeoutMinutes > 0
    ? configuredTimeoutMinutes * 60 * 1000
    : DEFAULT_TIMEOUT_MINUTES * 60 * 1000;

export function initializeSessionActivity(storage = globalThis.localStorage, now = Date.now) {
  storage.setItem(SESSION_ACTIVITY_KEY, String(now()));
}

export function clearStoredSession(storage = globalThis.localStorage) {
  storage.removeItem(SESSION_TOKEN_KEY);
  storage.removeItem(SESSION_ACTIVITY_KEY);
}

export function isStoredSessionInactive({
  storage = globalThis.localStorage,
  now = Date.now,
  inactivityTimeoutMs = SESSION_INACTIVITY_TIMEOUT_MS
} = {}) {
  const lastActivity = Number(storage.getItem(SESSION_ACTIVITY_KEY));
  return Number.isFinite(lastActivity) && lastActivity > 0 && now() - lastActivity >= inactivityTimeoutMs;
}

export function createSessionManager({
  onEnd,
  storage = globalThis.localStorage,
  eventTarget = globalThis.window,
  documentTarget = globalThis.document,
  now = Date.now,
  setTimer = globalThis.setTimeout,
  clearTimer = globalThis.clearTimeout,
  inactivityTimeoutMs = SESSION_INACTIVITY_TIMEOUT_MS,
  activityWriteIntervalMs = ACTIVITY_WRITE_INTERVAL_MS
} = {}) {
  let timerId = null;
  let started = false;
  let ending = false;
  let lastActivityWrite = 0;

  function readLastActivity() {
    const value = Number(storage.getItem(SESSION_ACTIVITY_KEY));
    return Number.isFinite(value) && value > 0 ? value : null;
  }

  function clearScheduledTimeout() {
    if (timerId !== null) {
      clearTimer(timerId);
      timerId = null;
    }
  }

  function scheduleExpiration() {
    clearScheduledTimeout();
    const lastActivity = readLastActivity();
    if (lastActivity === null) {
      return;
    }

    const remainingMs = Math.max(0, lastActivity + inactivityTimeoutMs - now());
    timerId = setTimer(checkExpiration, remainingMs);
  }

  function end(reason) {
    if (ending) {
      return;
    }

    ending = true;
    const token = storage.getItem(SESSION_TOKEN_KEY);
    stop();
    onEnd?.({ reason, token });
  }

  function checkExpiration() {
    const lastActivity = readLastActivity();
    if (lastActivity !== null && now() - lastActivity >= inactivityTimeoutMs) {
      end('inactive');
      return;
    }

    scheduleExpiration();
  }

  function markActivity() {
    if (!started || ending) {
      return;
    }

    const currentTime = now();
    const lastActivity = readLastActivity();
    if (lastActivity !== null && currentTime - lastActivity >= inactivityTimeoutMs) {
      end('inactive');
      return;
    }

    if (currentTime - lastActivityWrite < activityWriteIntervalMs) {
      return;
    }

    lastActivityWrite = currentTime;
    storage.setItem(SESSION_ACTIVITY_KEY, String(currentTime));
    scheduleExpiration();
  }

  function handleVisibilityChange() {
    if (!documentTarget.hidden) {
      markActivity();
    }
  }

  function handleStorage(event) {
    if (event.key === SESSION_TOKEN_KEY && event.oldValue && event.newValue !== event.oldValue) {
      end('external');
      return;
    }

    if (event.key === SESSION_ACTIVITY_KEY && event.newValue) {
      checkExpiration();
    }
  }

  function start() {
    if (started) {
      return;
    }

    started = true;
    ending = false;
    const lastActivity = readLastActivity();
    if (lastActivity === null) {
      initializeSessionActivity(storage, now);
    } else if (now() - lastActivity >= inactivityTimeoutMs) {
      end('inactive');
      return;
    }

    lastActivityWrite = readLastActivity() ?? now();
    eventTarget.addEventListener('pointerdown', markActivity);
    eventTarget.addEventListener('keydown', markActivity);
    eventTarget.addEventListener('scroll', markActivity, { passive: true });
    eventTarget.addEventListener('focus', markActivity);
    eventTarget.addEventListener('storage', handleStorage);
    documentTarget.addEventListener('visibilitychange', handleVisibilityChange);
    scheduleExpiration();
  }

  function stop() {
    clearScheduledTimeout();
    if (!started) {
      return;
    }

    started = false;
    eventTarget.removeEventListener('pointerdown', markActivity);
    eventTarget.removeEventListener('keydown', markActivity);
    eventTarget.removeEventListener('scroll', markActivity);
    eventTarget.removeEventListener('focus', markActivity);
    eventTarget.removeEventListener('storage', handleStorage);
    documentTarget.removeEventListener('visibilitychange', handleVisibilityChange);
  }

  return { start, stop, markActivity, checkExpiration };
}
