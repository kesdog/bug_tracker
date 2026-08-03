import React, { useEffect, useState } from 'react';
import { fetchAuditLogs } from '../api/auditLogs';

function valueOrDash(value) {
  return value === null || value === undefined || value === '' ? '-' : String(value);
}

function getLogTime(log) {
  return log.occurredAt || log.timestamp || log.createdAt || log.time || '';
}

function getLogActor(log) {
  return log.actor || log.actorName || log.actorUserId || log.actorId || log.actorEmail || '-';
}

function getLogActorType(log) {
  return log.actorType || log.actor_type || '-';
}

function getLogTicketId(log) {
  return log.ticketId || log.ticket_id || log.bugId || '-';
}

function getLogSummary(log) {
  if (log.summary || log.message) {
    return log.summary || log.message;
  }

  if (typeof log.details === 'string') {
    return log.details;
  }

  if (log.details && typeof log.details === 'object') {
    return JSON.stringify(log.details);
  }

  return '-';
}

export default function AuditLogsPage({ token, initialFilters = {} }) {
  const [logs, setLogs] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [filters, setFilters] = useState({ actorType: 'all', search: '', ticketId: '', action: '', ...initialFilters });
  const [submittedFilters, setSubmittedFilters] = useState(filters);

  useEffect(() => {
    let isActive = true;

    async function loadLogs() {
      setLoading(true);
      setError('');
      try {
        const nextLogs = await fetchAuditLogs(token, submittedFilters);
        if (isActive) {
          setLogs(Array.isArray(nextLogs) ? nextLogs : []);
        }
      } catch (err) {
        if (isActive) {
          setLogs([]);
          setError(err.message || 'Unable to load audit logs.');
        }
      } finally {
        if (isActive) {
          setLoading(false);
        }
      }
    }

    loadLogs();

    return () => {
      isActive = false;
    };
  }, [token, submittedFilters]);

  function updateFilter(key, value) {
    setFilters((current) => ({ ...current, [key]: value }));
  }

  function submitSearch(event) {
    event.preventDefault();
    setSubmittedFilters({ ...filters });
  }

  return (
    <section className="dashboard">
      <h2>Audit Logs</h2>
      <p className="subtitle">Search human and AI-agent activity across ticket and account events.</p>

      <form className="ticket-tools audit-log-tools" onSubmit={submitSearch}>
        <div className="audit-filter-grid">
          <label htmlFor="auditActorType">Actor type</label>
          <select id="auditActorType" value={filters.actorType} onChange={(event) => updateFilter('actorType', event.target.value)}>
            <option value="all">All actors</option>
            <option value="human">Human</option>
            <option value="agent">Agent</option>
            <option value="system">System</option>
          </select>

          <label htmlFor="auditSearch">Search logs</label>
          <input id="auditSearch" value={filters.search} onChange={(event) => updateFilter('search', event.target.value)} placeholder="Actor, action, message..." />

          <label htmlFor="auditTicketId">Ticket ID</label>
          <input id="auditTicketId" value={filters.ticketId} onChange={(event) => updateFilter('ticketId', event.target.value)} placeholder="bug_123" />

          <label htmlFor="auditAction">Action</label>
          <input id="auditAction" value={filters.action} onChange={(event) => updateFilter('action', event.target.value)} placeholder="ticket.created" />
        </div>

        <div className="ticket-search-row audit-action-row">
          <button type="submit" disabled={loading}>{loading ? 'Searching...' : 'Search Logs'}</button>
        </div>
      </form>

      {error ? <p role="alert" className="error-text">{error}</p> : null}
      {loading ? <div className="spinner" aria-label="loading audit logs" /> : null}
      {!loading && !error && logs.length === 0 ? <p className="dashboard-empty">No audit logs match this search.</p> : null}

      {!loading && logs.length > 0 ? (
        <div className="bug-table-wrap">
          <table className="bug-table audit-log-table">
            <thead>
              <tr>
                <th scope="col">Time</th>
                <th scope="col">Actor</th>
                <th scope="col">Actor Type</th>
                <th scope="col">Action</th>
                <th scope="col">Ticket ID</th>
                <th scope="col">Summary</th>
              </tr>
            </thead>
            <tbody>
              {logs.map((log, index) => (
                <tr key={log.id || `${getLogTime(log)}-${index}`}>
                  <td>{valueOrDash(getLogTime(log))}</td>
                  <td>{valueOrDash(getLogActor(log))}</td>
                  <td>{valueOrDash(getLogActorType(log))}</td>
                  <td>{valueOrDash(log.action)}</td>
                  <td>{valueOrDash(getLogTicketId(log))}</td>
                  <td>{valueOrDash(getLogSummary(log))}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
