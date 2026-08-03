import {
  API_BASE_URL,
  WRITE_REQUEST_TIMEOUT_MS,
  fetchWithTimeout,
  throwApiError
} from './bugs_transport';

function filenameFromContentDisposition(value, fallbackName) {
  if (typeof value !== 'string' || !value.trim()) {
    return fallbackName;
  }

  const encodedMatch = value.match(/filename\*=UTF-8''([^;]+)/i);
  if (encodedMatch?.[1]) {
    try {
      return decodeURIComponent(encodedMatch[1].replace(/"/g, '')).trim() || fallbackName;
    } catch {
      return fallbackName;
    }
  }

  const plainMatch = value.match(/filename="?([^";]+)"?/i);
  return plainMatch?.[1]?.trim() || fallbackName;
}

// Downloads attachment bytes through the authenticated ticket endpoint.
export async function downloadBugAttachment(accessToken, bugId, attachmentId, fallbackName = 'attachment') {
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/${encodeURIComponent(bugId)}/attachments/${encodeURIComponent(attachmentId)}`, {
    headers: { Authorization: `Bearer ${accessToken}` }
  });

  if (!response.ok) {
    await throwApiError(response, 'Unable to download attachment.');
  }

  const contentDisposition = response.headers?.get?.('content-disposition') || response.headers?.get?.('Content-Disposition') || '';
  return {
    blob: await response.blob(),
    filename: filenameFromContentDisposition(contentDisposition, fallbackName)
  };
}

// Exports the provided visible ticket IDs as a downloadable file payload.
export async function exportBugs(accessToken, format, ticketIds = []) {
  const normalizedFormat = format === 'csv' ? 'csv' : 'json';
  const safeTicketIds = Array.isArray(ticketIds) ? ticketIds.filter(Boolean).map(String) : [];
  const response = await fetchWithTimeout(`${API_BASE_URL}/api/bugs/export`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`
    },
    body: JSON.stringify({ format: normalizedFormat, ticketIds: safeTicketIds })
  }, WRITE_REQUEST_TIMEOUT_MS);

  if (!response.ok) {
    if (response.status === 403) {
      await throwApiError(response, 'Only senior developers and admins can export tickets.');
    }

    await throwApiError(response, 'Unable to export tickets right now.');
  }

  const fallbackName = `bug-tickets-${new Date().toISOString().slice(0, 10)}.${normalizedFormat}`;
  const contentDisposition = response.headers?.get?.('content-disposition') || response.headers?.get?.('Content-Disposition') || '';
  return {
    blob: await response.blob(),
    filename: filenameFromContentDisposition(contentDisposition, fallbackName)
  };
}
