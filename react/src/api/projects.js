import { authenticatedFetch } from './client';
import { API_BASE_URL } from './config';

async function readApiError(response, fallbackMessage) {
  try {
    const body = await response.json();
    if (body && typeof body.error === 'string' && body.error.trim()) {
      const error = body.error.trim();
      const hint = typeof body.hint === 'string' ? body.hint.trim() : '';
      return hint && !error.includes(hint) ? `${error} ${hint}` : error;
    }
  } catch {
    // Fall through to fallback.
  }

  return fallbackMessage;
}

export async function fetchProjects(accessToken) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/projects`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    throw new Error('Unable to load projects.');
  }

  return response.json();
}

export async function createProject(accessToken, name, visibility) {
  const payload = { name };
  if (visibility) {
    payload.visibility = visibility;
  }

  const response = await authenticatedFetch(`${API_BASE_URL}/api/projects`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify(payload)
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to create project.'));
  }

  return response.json();
}

export async function updateProjectVisibility(accessToken, projectId, visibility) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/projects/${encodeURIComponent(projectId)}/visibility`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ visibility })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to update project visibility.'));
  }

  if (response.status === 204) {
    return null;
  }

  return response.json();
}

export async function fetchProjectAllocations(accessToken) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/projects/allocations`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to load project allocations.'));
  }

  return response.json();
}

export async function updateProjectAllocations(accessToken, projectId, userIds) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/projects/${encodeURIComponent(projectId)}/allocations`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ userIds })
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to update project allocations.'));
  }
}

export async function fetchAllocatableProjectUsers(accessToken) {
  const response = await authenticatedFetch(`${API_BASE_URL}/api/projects/allocatable-users`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    throw new Error(await readApiError(response, 'Unable to load users.'));
  }

  return response.json();
}
