import {
  activeBugsKey,
  allocatedBugsKey,
  bugsQueryCache,
  closedBugsKey
} from './bugs_cache';
import {
  API_BASE_URL,
  appendBugFilters,
  fetchWithTimeout,
  throwApiError
} from './bugs_transport';

// Generic bugs listing helper used by active/closed shortcuts.
export async function fetchBugs(accessToken, status = 'active', limit = 10, search = '', filters = {}) {
  const normalizedSearch = typeof search === 'string' ? search.trim() : '';
  const cacheKey = status === 'closed' ? closedBugsKey(limit, normalizedSearch, filters) : activeBugsKey(limit, normalizedSearch, filters);

  return bugsQueryCache.getOrFetch(cacheKey, async () => {
    const params = new URLSearchParams({ status, limit: String(limit), sort: 'created_at_desc' });
    if (normalizedSearch) {
      params.set('search', normalizedSearch);
    }
    appendBugFilters(params, filters);

    const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs?${params.toString()}`, {
      headers: { Authorization: `Bearer ${accessToken}` }
    });

    if (!response.ok) {
      await throwApiError(response, 'Unable to load dashboard tickets.');
    }

    return response.json();
  });
}

function normalizeCursorEnvelope(payload) {
  if (Array.isArray(payload)) {
    return { items: payload, totalCount: payload.length, nextCursor: null, hasMore: false, summary: null };
  }
  return {
    items: Array.isArray(payload?.items) ? payload.items : [],
    totalCount: Number.isFinite(payload?.totalCount) ? payload.totalCount : 0,
    nextCursor: payload?.nextCursor || null,
    hasMore: Boolean(payload?.hasMore),
    summary: payload?.summary || null
  };
}

async function fetchCursorPage(accessToken, path, { status, limit = 25, cursor = '', search = '', filters = {}, sort = '' } = {}) {
  const params = new URLSearchParams();
  if (status) params.set('status', status);
  params.set('pagination', 'cursor');
  params.set('limit', String(limit));
  if (cursor) params.set('cursor', cursor);
  if (search?.trim()) params.set('search', search.trim());
  if (sort) params.set('sort', sort);
  appendBugFilters(params, filters);
  const response = await fetchWithTimeout(`${API_BASE_URL}${path}?${params.toString()}`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });
  if (!response.ok) await throwApiError(response, 'Unable to load ticket page.');
  return normalizeCursorEnvelope(await response.json());
}

export function fetchBugPage(accessToken, options = {}) {
  return fetchCursorPage(accessToken, '/api/bugs', options);
}

export function fetchAllocatedBugPage(accessToken, options = {}) {
  return fetchCursorPage(accessToken, '/api/bugs/allocated', options);
}

export async function fetchBugSummary(accessToken) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/summary`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });
  if (!response.ok) await throwApiError(response, 'Unable to load ticket summary.');
  return response.json();
}

// Fetches active dashboard/view tickets.
export function fetchActiveBugs(accessToken, limit = 10, search = '', filters = {}) {
  return fetchBugs(accessToken, 'active', limit, search, filters);
}

export async function fetchDashboardBugs(accessToken, limit = 10) {
  return bugsQueryCache.getOrFetch(activeBugsKey(limit), async () => {
    const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs?status=active&limit=${limit}&sort=created_at_desc&dashboard=true`, {
      headers: { Authorization: `Bearer ${accessToken}` }
    });

    if (!response.ok) {
      await throwApiError(response, 'Unable to load dashboard tickets.');
    }

    return response.json();
  });
}

// Fetches archived/closed tickets.
export function fetchClosedBugs(accessToken, limit = 50, search = '', filters = {}) {
  return fetchBugs(accessToken, 'closed', limit, search, filters);
}

// Fetches full details for one ticket.
export async function fetchBugById(accessToken, id) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/${encodeURIComponent(id)}`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    await throwApiError(response, 'Unable to load ticket report details.');
  }

  return response.json();
}

// Lists active users available for assignment (senior/admin endpoint).
export async function fetchAssignableUsers(accessToken) {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/assignees`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    if (response.status === 403) {
      await throwApiError(response, 'Only senior developers and admins can allocate bugs.');
    }

    await throwApiError(response, 'Unable to load assignable users.');
  }

  return response.json();
}

// Lists active tickets currently allocated to authenticated user.
export async function fetchAllocatedBugs(accessToken, limit = 100, search = '', filters = {}) {
  const normalizedSearch = typeof search === 'string' ? search.trim() : '';
  return bugsQueryCache.getOrFetch(allocatedBugsKey(accessToken, limit, normalizedSearch, filters), async () => {
    const params = new URLSearchParams({ limit: String(limit) });
    if (normalizedSearch) {
      params.set('search', normalizedSearch);
    }
    appendBugFilters(params, filters);

    const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/allocated?${params.toString()}`, {
      headers: { Authorization: `Bearer ${accessToken}` }
    });

    if (!response.ok) {
      await throwApiError(response, 'Unable to load allocated bugs.');
    }

    return response.json();
  });
}
