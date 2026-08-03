import { authenticatedFetch } from './client';
import { API_BASE_URL } from './config';

const REQUEST_TIMEOUT_MS = 8000;
const WRITE_REQUEST_TIMEOUT_MS = 12000;

async function fetchWithTimeout(url, options = {}, timeoutMs = REQUEST_TIMEOUT_MS) {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

  try {
    return await authenticatedFetch(url, { ...options, signal: controller.signal });
  } catch (err) {
    if (err && err.name === 'AbortError') {
      throw new Error('Notification request timed out.');
    }
    throw err;
  } finally {
    clearTimeout(timeoutId);
  }
}

export async function fetchNotifications(accessToken, { unreadOnly = true } = {}) {
  const params = new URLSearchParams({ unreadOnly: unreadOnly ? 'true' : 'false' });
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/notifications?${params.toString()}`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response?.ok) {
    const error = new Error('Notifications are not available.');
    error.status = response?.status;
    throw error;
  }

  return response.json();
}

export async function fetchUnreadNotificationCount(accessToken) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/notifications/unread-count`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response?.ok) {
    const error = new Error('Notification count is not available.');
    error.status = response?.status;
    throw error;
  }

  return response.json();
}

export async function markNotificationRead(accessToken, notificationId) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/notifications/${encodeURIComponent(notificationId)}/read`, {
    method: 'PATCH',
    headers: { Authorization: `Bearer ${accessToken}` }
  }, WRITE_REQUEST_TIMEOUT_MS);

  if (!response?.ok) {
    const error = new Error('Unable to mark notification read.');
    error.status = response?.status;
    throw error;
  }

  return response.status === 204 ? null : response.json();
}

export async function markAllNotificationsRead(accessToken) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/notifications/read-all`, {
    method: 'PATCH',
    headers: { Authorization: `Bearer ${accessToken}` }
  }, WRITE_REQUEST_TIMEOUT_MS);

  if (!response?.ok) {
    const error = new Error('Unable to mark all notifications read.');
    error.status = response?.status;
    throw error;
  }

  return response.json();
}
