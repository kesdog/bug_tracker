import React, { useEffect, useMemo, useState } from 'react';
import { cancelBug, closeBug, fetchAllocatedBugPage, fetchBugById, updateBugMetadata, updateBugReport, updateInitialBugReport } from '../api/bugs';
import BugReportFormPanel from '../components/BugReportFormPanel';
import CancelTicketDialog from '../components/CancelTicketDialog';
import CollapsibleFilters from '../components/CollapsibleFilters';
import ExportControls from '../components/ExportControls';
import { PriorityChip, SeverityChip } from '../components/MuiPrimitives';
import ReportPanel from '../components/ReportPanel';
import TicketTable, { getStoredTicketPageSize } from '../components/TicketTable';
import TicketMetadataPanel from '../components/TicketMetadataPanel';
import useTicketReport from '../hooks/useTicketReport';
import { getProjectName } from '../table_utils';
import { clearReviewedConflictFields, recoverTicketConflict } from '../concurrency';
import { writeAppUrlState } from '../url_state';

const ACTION_MODIFY = 'modify';
const ACTION_EDIT_INITIAL = 'edit-initial';
const ACTION_EDIT_METADATA = 'edit-metadata';
const ACTION_CLOSE = 'close';
const EMPTY_FILTERS = { priority: '', severity: '', tag: '', projectId: '' };

function getInitialReportText(ticket) {
  return ticket?.description || '';
}

function getEditableReportText(ticket) {
  return ticket?.postResolutionReport || ticket?.resolutionNotes || '';
}

function hasSolutionReport(ticket) {
  return Boolean((ticket?.postResolutionReport || ticket?.resolutionNotes || '').trim()) || (ticket?.resolutionReportImages || []).length > 0;
}

function getActionPanelCopy(actionType, ticket) {
  if (actionType === ACTION_EDIT_INITIAL) {
    return {
      title: 'Edit Bug Report',
      submitLabel: 'Save Bug Report',
      notesLabel: 'Bug Report'
    };
  }

  if (actionType === ACTION_MODIFY) {
    const hasSolution = hasSolutionReport(ticket);
    return {
      title: hasSolution ? 'Modify Solution Steps' : 'Create Solution',
      submitLabel: 'Save Report',
      notesLabel: 'Solution Steps'
    };
  }

  return {
    title: 'Close Bug',
    submitLabel: 'Close Bug',
    notesLabel: 'Solution Steps'
  };
}

function renderPriority(ticket) {
  const priority = ticket.priority || 'p2';
  return <PriorityChip value={priority} />;
}

export default function AllocatedPage({ token, userRole, userType = 'human', currentUserId = '', initialSearch = '', initialQuickFilter = 'all', initialFilters = EMPTY_FILTERS, initialTicketId = '' }) {
  const [tickets, setTickets] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [reloadKey, setReloadKey] = useState(0);
  const report = useTicketReport(token);
  const [actionTicket, setActionTicket] = useState(null);
  const [actionType, setActionType] = useState('');
  const [actionSubmitting, setActionSubmitting] = useState(false);
  const [actionError, setActionError] = useState('');
  const [searchText, setSearchText] = useState(initialSearch);
  const [searchTerm, setSearchTerm] = useState(initialSearch);
  const [quickFilter, setQuickFilter] = useState(initialQuickFilter);
  const [filterDraft, setFilterDraft] = useState({ ...EMPTY_FILTERS, ...initialFilters });
  const [serverFilters, setServerFilters] = useState({ ...EMPTY_FILTERS, ...initialFilters });
  const [actionConflict, setActionConflict] = useState(null);
  const [cancelTicket, setCancelTicket] = useState(null);
  const [cancelSubmitting, setCancelSubmitting] = useState(false);
  const [cancelError, setCancelError] = useState('');
  const [paginationModel, setPaginationModel] = useState(() => ({ page: 0, pageSize: getStoredTicketPageSize(currentUserId) }));
  const [totalCount, setTotalCount] = useState(0);
  const cursors = React.useRef([null]);

  useEffect(() => { if (initialTicketId) report.openReport({ id: initialTicketId }); }, [initialTicketId, report.openReport]);

  function closeActionPanels() {
    setActionTicket(null);
    setActionType('');
    setActionSubmitting(false);
    setActionError('');
    setActionConflict(null);
    setCancelTicket(null);
    setCancelSubmitting(false);
    setCancelError('');
  }

  // Fetches a full ticket payload (including report details/images) for modal usage.
  async function loadTicketDetails(ticketId) {
    return fetchBugById(token, ticketId);
  }

  // Shared helper to open one of the row menu actions.
  async function openActionForTicket(ticket, actionTypeValue) {
    if (!ticket) {
      return;
    }

    setActionError('');
    setActionConflict(null);
    try {
      const fullTicket = await loadTicketDetails(ticket.id);
      if (actionTypeValue === ACTION_CLOSE && !hasSolutionReport(fullTicket)) {
        setCancelTicket(fullTicket);
      } else {
        setActionTicket(fullTicket);
        setActionType(actionTypeValue);
      }
    } catch (err) {
      setActionError(err.message || 'Unable to load ticket details.');
    }
  }

  async function cancelTicketWithoutSolution(reason) {
    if (!cancelTicket) return;
    setCancelSubmitting(true);
    setCancelError('');
    try {
      await cancelBug(token, cancelTicket.id, reason, cancelTicket.version);
      setTickets((current) => current.filter((ticket) => ticket.id !== cancelTicket.id));
      closeActionPanels();
      setReloadKey((value) => value + 1);
    } catch (err) {
      setCancelError(err.message || 'Unable to cancel ticket.');
    } finally {
      setCancelSubmitting(false);
    }
  }

  // Saves either report edits or close-bug resolution details.
  async function submitActionForm({ text, images }) {
    if (!actionTicket) {
      return;
    }

    setActionSubmitting(true);
    setActionError('');
    try {
      if (actionType === ACTION_EDIT_INITIAL) {
        await updateInitialBugReport(token, actionTicket.id, text, images, actionConflict?.latestTicket?.version || actionTicket.version);
      } else if (actionType === ACTION_MODIFY) {
        await updateBugReport(token, actionTicket.id, text, images, actionConflict?.latestTicket?.version || actionTicket.version);
      } else if (actionType === ACTION_CLOSE) {
        await closeBug(token, actionTicket.id, text, images, actionConflict?.latestTicket?.version || actionTicket.version);
        setTickets((current) => current.filter((ticket) => ticket.id !== actionTicket.id));
      }

      closeActionPanels();
      setReloadKey((value) => value + 1);
    } catch (err) {
      const recovered = await recoverTicketConflict(err, token);
      if (recovered) {
        setActionConflict(recovered);
      } else {
        setActionError(err.message || 'Unable to save bug action.');
      }
    } finally {
      setActionSubmitting(false);
    }
  }

  async function submitMetadataForm(metadata) {
    if (!actionTicket) {
      return;
    }

    setActionSubmitting(true);
    setActionError('');
    try {
      await updateBugMetadata(token, actionTicket.id, metadata, actionConflict?.latestTicket?.version || actionTicket.version);
      closeActionPanels();
      setReloadKey((value) => value + 1);
    } catch (err) {
      const recovered = await recoverTicketConflict(err, token);
      if (recovered) {
        setActionConflict(recovered);
      } else {
        setActionError(err.message || 'Unable to update ticket metadata.');
      }
    } finally {
      setActionSubmitting(false);
    }
  }

  const columns = [
    { key: 'issueTitle', label: 'Bug', sortable: true, defaultDirection: 'asc' },
    { key: 'status', label: 'Status', sortable: true, defaultDirection: 'asc' },
    { key: 'assignedAt', label: 'Active Since', sortable: true, defaultDirection: 'desc' },
    {
      key: 'projectName',
      label: 'Project',
      sortable: true,
      defaultDirection: 'asc',
      render: (ticket) => getProjectName(ticket)
    },
    {
      key: 'severity',
      label: 'Severity',
      sortable: true,
      defaultDirection: 'desc',
      render: (ticket) => <SeverityChip value={ticket.severity} />
    },
    {
      key: 'priority',
      label: 'Priority',
      sortable: true,
      defaultDirection: 'desc',
      render: renderPriority
    },
  ];

  const rowMenuItems = [
    {
      key: 'view-reports',
      label: 'View Reports',
      onSelect: report.openReport
    },
    {
      key: ACTION_EDIT_INITIAL,
      label: 'Edit Bug Report',
      onSelect: (ticket) => openActionForTicket(ticket, ACTION_EDIT_INITIAL)
    },
    {
      key: ACTION_MODIFY,
      label: (ticket) => hasSolutionReport(ticket) ? 'Modify Solution Steps' : 'Create Solution',
      onSelect: (ticket) => openActionForTicket(ticket, ACTION_MODIFY)
    },
    {
      key: ACTION_EDIT_METADATA,
      label: 'Edit Metadata',
      onSelect: (ticket) => openActionForTicket(ticket, ACTION_EDIT_METADATA)
    },
    {
      key: ACTION_CLOSE,
      label: 'Close Bug',
      onSelect: (ticket) => openActionForTicket(ticket, ACTION_CLOSE)
    }
  ];

  useEffect(() => {
    // Cancels setState calls if the component unmounts before fetch completion.
    let isActive = true;

    async function loadAllocated() {
      setLoading(true);
      setError('');

      try {
          const nextPage = await fetchAllocatedBugPage(token, { limit: paginationModel.pageSize, cursor: cursors.current[paginationModel.page] || '', search: searchTerm, filters: serverFilters, sort: 'created_at_desc' });
        if (isActive) {
          setTickets(nextPage.items);
          setTotalCount(nextPage.totalCount);
          cursors.current[paginationModel.page + 1] = nextPage.nextCursor;
        }
      } catch (err) {
        if (isActive) {
          setTickets([]);
          setError(err.message || 'Unable to load allocated tickets.');
        }
      } finally {
        if (isActive) {
          setLoading(false);
        }
      }
    }

    loadAllocated();

    return () => {
      isActive = false;
    };
  }, [token, reloadKey, searchTerm, serverFilters, quickFilter, paginationModel]);

  const quickFilteredTickets = useMemo(() => {
    if (quickFilter === 'urgent') {
      return tickets.filter((ticket) => ticket.severity === 'urgent' || ticket.priority === 'p0');
    }

    if (quickFilter === 'recently-updated') {
      return [...tickets]
        .sort((a, b) => String(b.updatedAt || '').localeCompare(String(a.updatedAt || '')))
        .slice(0, 20);
    }

    return tickets;
  }, [quickFilter, tickets]);

  function submitSearch(event) {
    event.preventDefault();
    setSearchTerm(searchText.trim());
    writeAppUrlState({ view: 'allocated', search: searchText.trim(), quick: quickFilter, filters: serverFilters, ticket: '' });
    resetPage();
  }

  function submitServerFilters(event) {
    event.preventDefault();
    setServerFilters({ ...filterDraft });
    writeAppUrlState({ view: 'allocated', search: searchTerm, quick: quickFilter, filters: filterDraft, ticket: '' });
    resetPage();
  }

  function resetServerFilters() {
    setFilterDraft(EMPTY_FILTERS);
    setServerFilters(EMPTY_FILTERS);
    writeAppUrlState({ view: 'allocated', search: searchTerm, quick: quickFilter, filters: EMPTY_FILTERS, ticket: '' });
    resetPage();
  }

  function resetPage() { cursors.current = [null]; setPaginationModel((current) => ({ ...current, page: 0 })); }
  function changeQuickFilter(value) { setQuickFilter(value); writeAppUrlState({ view: 'allocated', search: searchTerm, quick: value, filters: serverFilters, ticket: '' }); resetPage(); }

  return (
    <section className="dashboard">
      <h2>Allocated Bugs</h2>
      <p className="subtitle">Review bugs currently assigned to you, update reports, and close resolved items.</p>

      {error ? (
        <p role="alert" className="error-text">
          {error}
        </p>
      ) : null}

      {loading ? <div className="spinner" aria-label="loading allocated bugs" /> : null}

      <form className="ticket-tools" onSubmit={submitSearch}>
        <label htmlFor="allocatedSearch">Search allocated bugs</label>
        <div className="ticket-search-row">
          <input id="allocatedSearch" value={searchText} onChange={(event) => setSearchText(event.target.value)} placeholder="Title, report, project, tag, priority..." />
          <button type="submit">Search</button>
        </div>
        <CollapsibleFilters
          id="allocated-ticket-filters"
          activeCount={Number(quickFilter !== 'all') + Object.values(serverFilters).filter(Boolean).length}
        >
          <div className="filter-row">
            <button type="button" aria-pressed={quickFilter === 'all'} className={`filter-button ${quickFilter === 'all' ? 'active' : ''}`} onClick={() => changeQuickFilter('all')}>All</button>
            <button type="button" aria-pressed={quickFilter === 'urgent'} className={`filter-button ${quickFilter === 'urgent' ? 'active' : ''}`} onClick={() => changeQuickFilter('urgent')}>Urgent</button>
            <button type="button" aria-pressed={quickFilter === 'recently-updated'} className={`filter-button ${quickFilter === 'recently-updated' ? 'active' : ''}`} onClick={() => changeQuickFilter('recently-updated')}>Recently Updated</button>
          </div>
          <div className="server-filter-grid" aria-label="Server allocated bug filters">
            <label htmlFor="allocated-priority-filter">Priority
              <select id="allocated-priority-filter" value={filterDraft.priority} onChange={(event) => setFilterDraft((current) => ({ ...current, priority: event.target.value }))}>
                <option value="">Any</option>
                <option value="p0">P0</option>
                <option value="p1">P1</option>
                <option value="p2">P2</option>
                <option value="p3">P3</option>
              </select>
            </label>
            <label htmlFor="allocated-severity-filter">Severity
              <select id="allocated-severity-filter" value={filterDraft.severity} onChange={(event) => setFilterDraft((current) => ({ ...current, severity: event.target.value }))}>
                <option value="">Any</option>
                <option value="low">low</option>
                <option value="mid">mid</option>
                <option value="high">high</option>
                <option value="urgent">urgent</option>
              </select>
            </label>
            <label htmlFor="allocated-tag-filter">Tag
              <input id="allocated-tag-filter" value={filterDraft.tag} onChange={(event) => setFilterDraft((current) => ({ ...current, tag: event.target.value }))} placeholder="back-end" />
            </label>
            <label htmlFor="allocated-project-filter">Project ID
              <input id="allocated-project-filter" value={filterDraft.projectId} onChange={(event) => setFilterDraft((current) => ({ ...current, projectId: event.target.value }))} placeholder="proj_123" />
            </label>
            <div className="server-filter-actions">
              <button type="button" onClick={submitServerFilters}>Apply Filters</button>
              <button type="button" className="tiny-action" onClick={resetServerFilters}>Reset Filters</button>
            </div>
          </div>
        </CollapsibleFilters>
        <ExportControls token={token} userRole={userRole} tickets={quickFilteredTickets} viewName="allocated bug" />
      </form>
      {quickFilter === 'recently-updated' ? <p className="subtitle">Recently Updated sorts this current page only.</p> : null}
      {quickFilter === 'urgent' ? <p className="subtitle">Urgent (urgent severity or P0) filters the current page because the server does not expose an OR filter.</p> : null}

      {!loading && !error && quickFilteredTickets.length === 0 ? <p className="dashboard-empty">No bugs have been allocated to you.</p> : null}

      {!error && (totalCount > 0 || quickFilteredTickets.length > 0) ? (
        <TicketTable tickets={quickFilteredTickets} columns={columns} rowMenuItems={rowMenuItems} loading={loading} currentUserId={currentUserId} rowCount={totalCount} paginationModel={paginationModel} onPaginationModelChange={(next) => { if (next.pageSize !== paginationModel.pageSize) cursors.current = [null]; setPaginationModel(next.pageSize !== paginationModel.pageSize ? { ...next, page: 0 } : next); }} />
      ) : null}

      {report.isOpen ? (
        <ReportPanel
          ticket={report.ticket}
          loading={report.loading}
          error={report.error}
           token={token}
           userType={userType}
          onAddComment={report.addComment}
          onClose={report.closeReport}
        />
      ) : null}

      {actionTicket && actionType === ACTION_EDIT_METADATA ? (
        <TicketMetadataPanel
          ticket={actionTicket}
          submitting={actionSubmitting}
          error={actionError}
          conflict={actionConflict?.conflict}
          latestTicket={actionConflict?.latestTicket}
          conflictRefreshError={actionConflict?.refreshError}
          onConflictReview={(fields) => setActionConflict((current) => current ? { ...current, conflict: clearReviewedConflictFields(current.conflict, fields) } : current)}
          onSubmit={submitMetadataForm}
          onClose={closeActionPanels}
        />
      ) : null}

      {actionTicket && actionType && actionType !== ACTION_EDIT_METADATA ? (
        (() => {
          const copy = getActionPanelCopy(actionType, actionTicket);
          const isInitialReportAction = actionType === ACTION_EDIT_INITIAL;
          const isResolutionReportAction = actionType === ACTION_MODIFY || actionType === ACTION_CLOSE;

          return (
            <BugReportFormPanel
              ticket={actionTicket}
              title={copy.title}
              submitLabel={copy.submitLabel}
              notesLabel={copy.notesLabel}
              initialText={isInitialReportAction ? getInitialReportText(actionTicket) : isResolutionReportAction ? getEditableReportText(actionTicket) : ''}
              initialImages={isInitialReportAction ? actionTicket.reportImages || [] : isResolutionReportAction ? actionTicket.resolutionReportImages || [] : []}
              submitting={actionSubmitting}
              error={actionError}
              conflict={actionConflict?.conflict}
              latestTicket={actionConflict?.latestTicket}
              conflictFields={isInitialReportAction ? ['description', 'reportImages'] : ['postResolutionReport', 'resolutionNotes', 'resolutionReportImages']}
              conflictRefreshError={actionConflict?.refreshError}
              actionKind={actionType === ACTION_CLOSE ? 'close' : ''}
              onConflictReview={(fields) => setActionConflict((current) => current ? { ...current, conflict: clearReviewedConflictFields(current.conflict, fields) } : current)}
              onSubmit={submitActionForm}
              onClose={closeActionPanels}
            />
          );
        })()
      ) : null}
      <CancelTicketDialog ticket={cancelTicket} submitting={cancelSubmitting} error={cancelError} onCancel={cancelTicketWithoutSolution} onClose={closeActionPanels} />
    </section>
  );
}
