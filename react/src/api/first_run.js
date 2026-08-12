import { authenticatedFetch } from './client';
import { API_BASE_URL } from './config';

async function request(accessToken, path, options = {}) {
  const response = await authenticatedFetch(`${API_BASE_URL}${path}`, {
    ...options,
    headers: { Authorization: `Bearer ${accessToken}`, ...options.headers }
  });
  if (!response.ok) {
    let message = 'Unable to complete first-run setup.';
    try {
      const body = await response.json();
      if (body?.error) message = body.error;
    } catch {
      // Use the generic error when the response has no JSON body.
    }
    throw new Error(message);
  }
  return response.status === 204 ? null : response.json();
}

export function fetchFirstRunStatus(accessToken) {
  return request(accessToken, '/api/first-run/status');
}

export function changeFirstRunPassword(accessToken, newPassword) {
  return request(accessToken, '/api/first-run/password', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ newPassword })
  });
}

export function completeFirstRun(accessToken, humanTokenTtlMinutes, agentOathTtlDays) {
  return request(accessToken, '/api/first-run/complete', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ humanTokenTtlMinutes, agentOathTtlDays })
  });
}
