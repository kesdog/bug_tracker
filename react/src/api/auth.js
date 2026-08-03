import { authenticatedFetch } from './client';
import { API_BASE_URL } from './config';

export async function login(email, password) {
  const response = await fetch(`${API_BASE_URL}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password })
  });

  if (!response.ok) {
    if (response.status === 401) {
      throw new Error('Invalid email or password.');
    }
    throw new Error(await readApiError(response, 'Unable to login right now.'));
  }

  return response.json();
}

export async function fetchMe(accessToken) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/me`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    throw new Error('Session is invalid or expired.');
  }

  return response.json();
}

export async function logout(accessToken) {
  await fetch(`${API_BASE_URL}/api/auth/logout`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${accessToken}` }
  });
}

async function readApiError(response, fallbackMessage) {
  if (response.status === 429) {
    const retryAfterSeconds = Number(response.headers?.get?.('Retry-After'));
    if (Number.isFinite(retryAfterSeconds) && retryAfterSeconds > 0) {
      const retryAfterMinutes = Math.max(1, Math.ceil(retryAfterSeconds / 60));
      return `Too many attempts. Try again in ${retryAfterMinutes} minute${retryAfterMinutes === 1 ? '' : 's'}.`;
    }

    return 'Too many attempts. Please try again later.';
  }

  try {
    const body = await response.json();
    if (body && typeof body.error === 'string' && body.error.trim()) {
      return body.error;
    }
  } catch {
    // fall through
  }

  return fallbackMessage;
}

export async function setupPassword(email, token, newPassword) {
  const response = await fetch(`${API_BASE_URL}/api/auth/setup-password`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, token, newPassword })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to set password right now.'));
  }
}

export async function requestAccess(email, requestType) {
  const response = await fetch(`${API_BASE_URL}/api/auth/request-access`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, requestType })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to submit access request.'));
  }

  return response.json();
}

export async function requestCredentialRecovery(email, requestType) {
  const response = await fetch(`${API_BASE_URL}/api/auth/request-credential-recovery`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, requestType })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to submit credential recovery request.'));
  }

  return response.json();
}
