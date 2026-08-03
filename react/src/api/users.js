import { authenticatedFetch } from './client';
import { API_BASE_URL } from './config';

async function readApiError(response, fallbackMessage) {
  try {
    const body = await response.json();
    if (body && typeof body.error === 'string' && body.error.trim()) {
      return body.error;
    }
  } catch {
    // Ignore parse errors and return fallback.
  }

  return fallbackMessage;
}

export async function fetchUsers(accessToken) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/users`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to load users.'));
  }

  return response.json();
}

export async function updateUserRole(accessToken, userId, role) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/users/${encodeURIComponent(userId)}/role`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ role })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to update user role.'));
  }

  return response.json();
}

export async function updateUserUsername(accessToken, userId, username) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/users/${encodeURIComponent(userId)}/username`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ username })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to update username.'));
  }

  return response.json();
}

export async function fetchRequests(accessToken) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/requests`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to load requests.'));
  }

  return response.json();
}

export async function createRequest(accessToken, email, requestType) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/requests`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ email, requestType })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to create request.'));
  }

  return response.json();
}

export async function updateRequestUsername(accessToken, requestId, username) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/requests/${encodeURIComponent(requestId)}/username`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ username })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to update username.'));
  }

  return response.json();
}

export async function issueSetupLink(accessToken, requestId) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/requests/${encodeURIComponent(requestId)}/issue-setup-link`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to issue setup link.'));
  }

  return response.json();
}

export async function issuePasswordReset(accessToken, recoveryId) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/recovery-requests/${encodeURIComponent(recoveryId)}/issue-password-reset`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to issue password reset link.'));
  }

  return response.json();
}

export async function issueRecoveryAgentApiKey(accessToken, recoveryId, activeDays) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/recovery-requests/${encodeURIComponent(recoveryId)}/issue-api-key`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${accessToken}` },
    body: JSON.stringify({ activeDays })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to issue recovery oath token.'));
  }

  return response.json();
}

export async function issueAgentApiKey(accessToken, requestId, activeDays) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/requests/${encodeURIComponent(requestId)}/issue-api-key`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ activeDays })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to issue API key.'));
  }

  return response.json();
}

export async function reissueAgentApiKey(accessToken, userId, activeDays) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/users/${encodeURIComponent(userId)}/issue-api-key`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ activeDays })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to reissue API key.'));
  }

  return response.json();
}

export async function removeRequest(accessToken, requestId) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/auth/requests/${encodeURIComponent(requestId)}`, {
    method: 'DELETE',
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to remove request.'));
  }
}
