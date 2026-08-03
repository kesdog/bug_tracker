import { authenticatedFetch } from './client';
import { API_BASE_URL } from './config';

export { API_BASE_URL };

export const REQUEST_TIMEOUT_MS = 12000;
export const WRITE_REQUEST_TIMEOUT_MS = 30000;

export class ApiError extends Error {
  constructor(message, { status = 0, errorCode = '', payload = null, uncertain = false } = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.errorCode = errorCode;
    this.payload = payload;
    this.uncertain = uncertain;
    this.steps = Array.isArray(payload?.steps) ? payload.steps : [];
    this.contacts = Array.isArray(payload?.contacts) ? payload.contacts : [];
    this.requestAccessPath = typeof payload?.requestAccessPath === 'string' ? payload.requestAccessPath : '';
  }
}

export function appendBugFilters(params, filters = {}) {
  const allowedFilterKeys = ['priority', 'severity', 'tag', 'projectId', 'assigneeUserId', 'reporterUserId'];
  allowedFilterKeys.forEach((key) => {
    const value = filters?.[key];
    if (value !== undefined && value !== null && String(value).trim()) {
      params.set(key, String(value).trim());
    }
  });
}

export async function createApiError(response, fallbackMessage) {
  let payload = null;
  try {
    payload = await response.json();
    if (payload && (typeof payload.error === 'string' || typeof payload.message === 'string')) {
      const error = (payload.message || payload.error || fallbackMessage).trim();
      const hint = typeof payload.hint === 'string' ? payload.hint.trim() : '';
      return new ApiError(hint && !error.includes(hint) ? `${error} ${hint}` : error, {
        status: response.status,
        errorCode: typeof payload.errorCode === 'string' ? payload.errorCode : '',
        payload
      });
    }
  } catch {
    // Ignore parse errors and fall back to generic message.
  }

  return new ApiError(fallbackMessage, { status: response.status, payload });
}

export async function throwApiError(response, fallbackMessage) {
  throw await createApiError(response, fallbackMessage);
}

// Shared fetch wrapper with timeout and temporary perf instrumentation.
export async function fetchWithTimeout(url, options = {}, timeoutMs = REQUEST_TIMEOUT_MS) {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
  const startedAt = typeof performance !== 'undefined' ? performance.now() : Date.now();

  try {
    const response = await authenticatedFetch(url, { ...options, signal: controller.signal });

    const finishedAt = typeof performance !== 'undefined' ? performance.now() : Date.now();
    const elapsedMs = Math.round(finishedAt - startedAt);

    if (import.meta.env.DEV) {
      // TEMP PERF CHECK: remove this once mobile performance tuning is done.
      if (elapsedMs > 1500) {
        console.warn(`[PERF CHECK - TEMP] Slow request (${elapsedMs}ms): ${url}`);
      } else {
        console.info(`[PERF CHECK - TEMP] Request (${elapsedMs}ms): ${url}`);
      }
    }

    return response;
  } catch (err) {
    if (err && err.name === 'AbortError') {
      const method = String(options.method || 'GET').toUpperCase();
      const uncertain = method !== 'GET' && method !== 'HEAD';
      throw new ApiError(
        uncertain
          ? 'Request timed out. The server may have completed this change; reload the ticket before retrying.'
          : 'Request timed out while waiting for the server. Please retry.',
        { errorCode: 'request_timeout', uncertain }
      );
    }
    throw err;
  } finally {
    clearTimeout(timeoutId);
  }
}
