import { authenticatedFetch } from './client';
import { API_BASE_URL } from './config';

const REQUEST_TIMEOUT_MS = 12000;

async function fetchWithTimeout(url, options = {}, timeoutMs = REQUEST_TIMEOUT_MS) {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

  try {
    return await authenticatedFetch(url, { ...options, signal: controller.signal });
  } catch (err) {
    if (err?.name === 'AbortError') {
      throw new Error('Request timed out while waiting for the server. Please retry.');
    }
    throw err;
  } finally {
    clearTimeout(timeoutId);
  }
}

async function readApiError(response, fallbackMessage) {
  try {
    const body = await response.json();
    if (body && typeof body.error === 'string' && body.error.trim()) {
      return body.error;
    }
  } catch {
    // Ignore parse errors and use fallback.
  }

  return fallbackMessage;
}

export async function fetchAuditLogs(accessToken, filters = {}) {
  const params = new URLSearchParams({ limit: String(filters.limit || 50) });
  const actorType = filters.actorType === 'human' || filters.actorType === 'agent' ? filters.actorType : '';
  const search = typeof filters.search === 'string' ? filters.search.trim() : '';
  const ticketId = typeof filters.ticketId === 'string' ? filters.ticketId.trim() : '';
  const action = typeof filters.action === 'string' ? filters.action.trim() : '';

  if (actorType) {
    params.set('actorType', actorType);
  }
  if (search) {
    params.set('search', search);
  }
  if (ticketId) {
    params.set('ticketId', ticketId);
  }
  if (action) {
    params.set('action', action);
  }

  const response = await fetchWithTimeout(`${API_BASE_URL}/api/audit-logs?${params.toString()}`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    if (response.status === 403) {
      throw new Error('Only admins can view audit logs.');
    }
    throw new Error(await readApiError(response, 'Unable to load audit logs.'));
  }

  const body = await response.json();
  return Array.isArray(body) ? body : body.logs || body.items || [];
}
