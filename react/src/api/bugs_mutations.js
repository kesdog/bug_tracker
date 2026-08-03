import { invalidateBugCacheScopes } from './bugs_cache';
import { authenticatedFetch } from './client';
import {
  API_BASE_URL,
  ApiError,
  WRITE_REQUEST_TIMEOUT_MS,
  createApiError,
  fetchWithTimeout,
  throwApiError
} from './bugs_transport';

// Creates a new bug with report text/images payload.
export async function createBug(accessToken, payload) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/bugs`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    if (response.status === 400) {
      throw await createApiError(response, 'Please fix the highlighted validation errors.');
    }

    await throwApiError(response, 'Unable to create bug right now.');
  }

  invalidateBugCacheScopes(['active']);

  return response.json();
}

// Assigns one bug to one target user.
export async function allocateBug(accessToken, bugId, assigneeUserId, expectedVersion) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/${encodeURIComponent(bugId)}/allocate`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ assigneeUserId, expectedVersion })
  }, WRITE_REQUEST_TIMEOUT_MS);

  if (!response.ok) {
    if (response.status === 403) {
      await throwApiError(response, 'Only senior developers and admins can allocate bugs.');
    }

    if (response.status === 404) {
      await throwApiError(response, 'Ticket no longer exists.');
    }

    await throwApiError(response, 'Unable to allocate this bug right now.');
  }

  invalidateBugCacheScopes(['active', 'allocated']);

  return response.json();
}

// Assigns all currently visible ticket IDs to one target user.
export async function bulkAllocateBugs(accessToken, items = [], assigneeUserId) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/bulk-allocate`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ items, assigneeUserId })
  }, WRITE_REQUEST_TIMEOUT_MS);

  if (!response.ok) {
    if (response.status === 403) {
      await throwApiError(response, 'Only senior developers and admins can bulk assign bugs.');
    }

    await throwApiError(response, 'Unable to bulk assign visible tickets right now.');
  }

  invalidateBugCacheScopes(['active', 'allocated']);
  return response.json();
}

export async function addBugComment(accessToken, bugId, body, recipientUserId = '') {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/${encodeURIComponent(bugId)}/comments`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify(recipientUserId ? { body, recipientUserId } : { body })
  }, WRITE_REQUEST_TIMEOUT_MS);

  if (!response.ok) {
    if (response.status === 403) {
      await throwApiError(response, 'You do not have permission to comment on this ticket.');
    }

    await throwApiError(response, 'Unable to add comment.');
  }

  invalidateBugCacheScopes(['active', 'closed', 'allocated']);
  return response.json();
}

export async function requestTicketAccess(accessToken, requestAccessPath, reason) {
  if (!requestAccessPath || !requestAccessPath.startsWith('/')) {
    throw new ApiError('A valid access request path was not provided.', { errorCode: 'invalid_request_access_path' });
  }
  const response = await fetchWithTimeout(`${API_BASE_URL}${requestAccessPath}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${accessToken}` },
    body: JSON.stringify({ reason })
  }, WRITE_REQUEST_TIMEOUT_MS);
  if (!response.ok) await throwApiError(response, 'Unable to request project access.');
  return response.status === 204 ? null : response.json();
}

// Saves updated report body/images for a ticket.
export async function updateInitialBugReport(accessToken, bugId, reportText, reportImages = [], expectedVersion) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/${encodeURIComponent(bugId)}/initial-report`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ reportText, reportImages, expectedVersion })
  }, WRITE_REQUEST_TIMEOUT_MS);

  if (!response.ok) {
    if (response.status === 403) {
      await throwApiError(response, 'You do not have permission to edit this bug report.');
    }

    await throwApiError(response, 'Unable to edit bug report.');
  }

  invalidateBugCacheScopes(['active', 'allocated']);

  return response.json();
}

// Saves updated solution report body/images for a ticket.
export async function updateBugReport(accessToken, bugId, reportText, reportImages = [], expectedVersion) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/${encodeURIComponent(bugId)}/report`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ reportText, reportImages, expectedVersion })
  }, WRITE_REQUEST_TIMEOUT_MS);

  if (!response.ok) {
    if (response.status === 403) {
      await throwApiError(response, 'You do not have permission to modify this bug report.');
    }

    await throwApiError(response, 'Unable to modify bug report.');
  }

  invalidateBugCacheScopes(['active', 'closed', 'allocated']);

  return response.json();
}

export async function updateBugMetadata(accessToken, bugId, metadata, expectedVersion) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/${encodeURIComponent(bugId)}/metadata`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ ...metadata, expectedVersion })
  }, WRITE_REQUEST_TIMEOUT_MS);

  if (!response.ok) {
    if (response.status === 403) {
      await throwApiError(response, 'You do not have permission to edit ticket metadata.');
    }

    await throwApiError(response, 'Unable to update ticket metadata.');
  }

  invalidateBugCacheScopes(['active', 'closed', 'allocated']);
  return response.json();
}

export async function reopenBug(accessToken, bugId, reason, expectedVersion) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/${encodeURIComponent(bugId)}/reopen`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ reason, expectedVersion })
  }, WRITE_REQUEST_TIMEOUT_MS);

  if (!response.ok) {
    if (response.status === 403) {
      await throwApiError(response, 'You do not have permission to reopen this ticket.');
    }

    await throwApiError(response, 'Unable to reopen ticket.');
  }

  invalidateBugCacheScopes(['active', 'closed', 'allocated']);
  return response.json();
}

// Closes a ticket with final resolution notes/images.
export async function closeBug(accessToken, bugId, resolutionNotes, reportImages = [], expectedVersion) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/${encodeURIComponent(bugId)}/close`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ resolutionNotes, reportImages, expectedVersion })
  }, WRITE_REQUEST_TIMEOUT_MS);

  if (!response.ok) {
    if (response.status === 400) {
      throw await createApiError(response, 'Unable to close bug.');
    }

    if (response.status === 403) {
      await throwApiError(response, 'You do not have permission to close this bug.');
    }

    await throwApiError(response, 'Unable to close bug.');
  }

  invalidateBugCacheScopes(['active', 'closed', 'allocated']);

  return response.json();
}

export async function cancelBug(accessToken, bugId, reason, expectedVersion) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/${encodeURIComponent(bugId)}/cancel`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${accessToken}` },
    body: JSON.stringify({ reason, expectedVersion })
  }, WRITE_REQUEST_TIMEOUT_MS);
  if (!response.ok) await throwApiError(response, 'Unable to cancel bug.');
  invalidateBugCacheScopes(['active', 'closed', 'allocated']);
  return response.json();
}
