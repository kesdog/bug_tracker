import React, { useState } from 'react';
import { exportBugs } from '../api/bugs';

const EXPORT_ROLES = new Set(['senior', 'admin']);

export function canExportTickets(userRole) {
  return EXPORT_ROLES.has(userRole);
}

function downloadBlob(blob, filename) {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

export default function ExportControls({ token, userRole, tickets, viewName }) {
  const [exportingFormat, setExportingFormat] = useState('');
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  if (!canExportTickets(userRole)) {
    return null;
  }

  const visibleTicketIds = Array.isArray(tickets) ? tickets.map((ticket) => ticket.id).filter(Boolean) : [];
  const disabled = visibleTicketIds.length === 0 || Boolean(exportingFormat);

  async function handleExport(format) {
    if (visibleTicketIds.length === 0) {
      return;
    }

    setExportingFormat(format);
    setError('');
    setSuccess('');
    try {
      const result = await exportBugs(token, format, visibleTicketIds);
      downloadBlob(result.blob, result.filename);
      setSuccess(`Exported ${visibleTicketIds.length} visible ${viewName || 'ticket'}${visibleTicketIds.length === 1 ? '' : 's'}.`);
    } catch (err) {
      setError(err.message || 'Unable to export visible tickets.');
    } finally {
      setExportingFormat('');
    }
  }

  return (
    <div className="export-controls" aria-label="Export visible tickets">
      <span className="export-controls-label">Export visible</span>
      <button type="button" className="filter-button" onClick={() => handleExport('csv')} disabled={disabled}>
        {exportingFormat === 'csv' ? 'Exporting CSV...' : 'CSV'}
      </button>
      <button type="button" className="filter-button" onClick={() => handleExport('json')} disabled={disabled}>
        {exportingFormat === 'json' ? 'Exporting JSON...' : 'JSON'}
      </button>
      {error ? <span role="alert" className="export-status error-text">{error}</span> : null}
      {success ? <span className="export-status success-text">{success}</span> : null}
    </div>
  );
}
