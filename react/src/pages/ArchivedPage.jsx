import React, { useEffect, useMemo, useState } from 'react';
import { fetchBugPage, reopenBug } from '../api/bugs';
import CollapsibleFilters from '../components/CollapsibleFilters';
import ExportControls from '../components/ExportControls';
import { PriorityChip, SeverityChip } from '../components/MuiPrimitives';
import ReopenTicketPanel from '../components/ReopenTicketPanel';
import ReportPanel from '../components/ReportPanel';
import TicketTable, { getStoredTicketPageSize } from '../components/TicketTable';
import useTicketReport from '../hooks/useTicketReport';
import { getProjectName } from '../table_utils';
import { recoverTicketConflict } from '../concurrency';
import { writeAppUrlState } from '../url_state';
import { useI18n } from '../i18n';

const EMPTY_FILTERS = { priority: '', severity: '', tag: '', projectId: '', assigneeUserId: '', reporterUserId: '' };

function renderPriority(ticket) {
  const priority = ticket.priority || 'p2';
  return <PriorityChip value={priority} />;
}

export default function ArchivedPage({ token, userRole, userType = 'human', currentUserId = '', initialFilters = EMPTY_FILTERS, initialSearch = '', initialQuickFilter = 'all', initialTicketId = '' }) {
  const { t } = useI18n();
  const [tickets, setTickets] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const report = useTicketReport(token);
  const [searchText, setSearchText] = useState(initialSearch);
  const [searchTerm, setSearchTerm] = useState(initialSearch);
  const [quickFilter, setQuickFilter] = useState(initialQuickFilter);
  const [filterDraft, setFilterDraft] = useState({ ...EMPTY_FILTERS, ...initialFilters });
  const [serverFilters, setServerFilters] = useState({ ...EMPTY_FILTERS, ...initialFilters });
  const [reopenTicket, setReopenTicket] = useState(null);
  const [reopenSubmitting, setReopenSubmitting] = useState(false);
  const [reopenError, setReopenError] = useState('');
  const [reopenConflict, setReopenConflict] = useState(null);
  const [paginationModel, setPaginationModel] = useState(() => ({ page: 0, pageSize: getStoredTicketPageSize(currentUserId) }));
  const [totalCount, setTotalCount] = useState(0);
  const cursors = React.useRef([null]);

  useEffect(() => { if (initialTicketId) report.openReport({ id: initialTicketId }); }, [initialTicketId, report.openReport]);

  const canReopenByRole = userRole === 'senior' || userRole === 'admin';

  function canReopenTicket(ticket) {
    return canReopenByRole || (!!currentUserId && (ticket.assigneeUserId === currentUserId || ticket.reporterUserId === currentUserId));
  }

  async function submitReopen(reason) {
    if (!reopenTicket) {
      return;
    }

    setReopenSubmitting(true);
    setReopenError('');
    try {
      await reopenBug(token, reopenTicket.id, reason, reopenConflict?.latestTicket?.version || reopenTicket.version);
      setTickets((current) => current.filter((ticket) => ticket.id !== reopenTicket.id));
      setReopenTicket(null);
    } catch (err) {
      const recovered = await recoverTicketConflict(err, token);
      if (recovered) {
        setReopenConflict(recovered);
      } else {
         setReopenError(err.message || t('pages.archived.errors.reopenTicket', 'Unable to reopen ticket.'));
      }
    } finally {
      setReopenSubmitting(false);
    }
  }

  const columns = [
    { key: 'issueTitle', label: t('common.bug', 'Bug'), sortable: true, defaultDirection: 'asc' },
    { key: 'reporterUserId', label: t('common.reportedBy', 'Reported By'), sortable: true, defaultDirection: 'asc' },
    { key: 'closeDate', label: t('common.resolvedAt', 'Resolved At'), sortable: true, defaultDirection: 'desc' },
    {
      key: 'projectName',
      label: t('common.project', 'Project'),
      sortable: true,
      defaultDirection: 'asc',
      render: (ticket) => getProjectName(ticket)
    },
    {
      key: 'severity',
      label: t('common.severity', 'Severity'),
      sortable: true,
      defaultDirection: 'desc',
      render: (ticket) => <SeverityChip value={ticket.severity} />
    },
    {
      key: 'priority',
      label: t('common.priority', 'Priority'),
      sortable: true,
      defaultDirection: 'desc',
      render: renderPriority
    },
    {
      key: 'resolvedBy',
      label: t('common.resolvedBy', 'Resolved By'),
      sortable: true,
      defaultDirection: 'asc',
      render: (ticket) => ticket.resolvedByUserId || ticket.assigneeUserId || '-'
    }
  ];

  const rowMenuItems = [
    {
      key: 'view-reports',
      label: t('common.viewReports', 'View Reports'),
      onSelect: report.openReport
    },
    {
      key: 'reopen',
      label: t('common.reopen', 'Reopen'),
      shouldShow: canReopenTicket,
      onSelect: (ticket) => {
        setReopenError('');
        setReopenConflict(null);
        setReopenTicket(ticket);
      }
    }
  ];

  useEffect(() => {
    let isActive = true;

    async function loadArchived() {
      setLoading(true);
      setError('');
      try {
        const nextPage = await fetchBugPage(token, { status: 'closed', limit: paginationModel.pageSize, cursor: cursors.current[paginationModel.page] || '', search: searchTerm, filters: serverFilters, sort: 'created_at_desc' });
        if (isActive) {
          setTickets(nextPage.items);
          setTotalCount(nextPage.totalCount);
          cursors.current[paginationModel.page + 1] = nextPage.nextCursor;
        }
      } catch (err) {
        if (isActive) {
          setTickets([]);
           setError(err.message || t('pages.archived.errors.loadTickets', 'Unable to load archived tickets.'));
        }
      } finally {
        if (isActive) {
          setLoading(false);
        }
      }
    }

    loadArchived();

    return () => {
      isActive = false;
    };
  }, [token, searchTerm, serverFilters, quickFilter, paginationModel]);

  const quickFilteredTickets = useMemo(() => {
    if (quickFilter === 'urgent') {
      return tickets.filter((ticket) => ticket.severity === 'urgent' || ticket.priority === 'p0');
    }

    if (quickFilter === 'closed-this-week') {
      const weekAgo = Date.now() - 7 * 24 * 60 * 60 * 1000;
      return tickets.filter((ticket) => {
        if (!ticket.closeDate) {
          return false;
        }
        const parsed = new Date(`${ticket.closeDate.replace(' ', 'T')}Z`);
        return !Number.isNaN(parsed.getTime()) && parsed.getTime() >= weekAgo;
      });
    }

    return tickets;
  }, [quickFilter, tickets]);

  function submitSearch(event) {
    event.preventDefault();
    setSearchTerm(searchText.trim());
    writeAppUrlState({ view: 'archived', search: searchText.trim(), quick: quickFilter, filters: serverFilters, ticket: '' });
    resetPage();
  }

  function submitServerFilters(event) {
    event.preventDefault();
    setServerFilters({ ...filterDraft });
    writeAppUrlState({ view: 'archived', search: searchTerm, quick: quickFilter, filters: filterDraft, ticket: '' });
    resetPage();
  }

  function resetServerFilters() {
    setFilterDraft(EMPTY_FILTERS);
    setServerFilters(EMPTY_FILTERS);
    writeAppUrlState({ view: 'archived', search: searchTerm, quick: quickFilter, filters: EMPTY_FILTERS, ticket: '' });
    resetPage();
  }

  function resetPage() { cursors.current = [null]; setPaginationModel((current) => ({ ...current, page: 0 })); }
  function changeQuickFilter(value) { setQuickFilter(value); writeAppUrlState({ view: 'archived', search: searchTerm, quick: value, filters: serverFilters, ticket: '' }); resetPage(); }

  return (
    <section className="dashboard">
       <h2>{t('pages.archived.title', 'Archived Tickets')}</h2>
       <p className="subtitle">{t('pages.archived.subtitle', 'Audit closed bugs, final reports, and historical resolution details.')}</p>

      {error ? (
        <p role="alert" className="error-text">
          {error}
        </p>
      ) : null}

       {loading ? <div className="spinner" aria-label={t('pages.archived.loading', 'loading archived tickets')} /> : null}

      <form className="ticket-tools" onSubmit={submitSearch}>
         <label htmlFor="archivedSearch">{t('pages.archived.searchLabel', 'Search archived tickets')}</label>
        <div className="ticket-search-row">
           <input id="archivedSearch" value={searchText} onChange={(event) => setSearchText(event.target.value)} placeholder={t('pages.archived.searchPlaceholder', 'Title, report, project, tag, priority...')} />
           <button type="submit">{t('common.search', 'Search')}</button>
        </div>
        <CollapsibleFilters
          id="archived-ticket-filters"
          activeCount={Number(quickFilter !== 'all') + Object.values(serverFilters).filter(Boolean).length}
        >
          <div className="filter-row">
             <button type="button" aria-pressed={quickFilter === 'all'} className={`filter-button ${quickFilter === 'all' ? 'active' : ''}`} onClick={() => changeQuickFilter('all')}>{t('common.all', 'All')}</button>
             <button type="button" aria-pressed={quickFilter === 'urgent'} className={`filter-button ${quickFilter === 'urgent' ? 'active' : ''}`} onClick={() => changeQuickFilter('urgent')}>{t('common.urgent', 'Urgent')}</button>
             <button type="button" aria-pressed={quickFilter === 'closed-this-week'} className={`filter-button ${quickFilter === 'closed-this-week' ? 'active' : ''}`} onClick={() => changeQuickFilter('closed-this-week')}>{t('pages.archived.closedThisWeek', 'Closed This Week')}</button>
          </div>
          <div className="server-filter-grid" aria-label="Server archived ticket filters">
            <label htmlFor="archived-priority-filter">Priority
              <select id="archived-priority-filter" value={filterDraft.priority} onChange={(event) => setFilterDraft((current) => ({ ...current, priority: event.target.value }))}>
                <option value="">{t('common.any', 'Any')}</option>
                <option value="p0">P0</option>
                <option value="p1">P1</option>
                <option value="p2">P2</option>
                <option value="p3">P3</option>
              </select>
            </label>
            <label htmlFor="archived-severity-filter">Severity
              <select id="archived-severity-filter" value={filterDraft.severity} onChange={(event) => setFilterDraft((current) => ({ ...current, severity: event.target.value }))}>
                <option value="">{t('common.any', 'Any')}</option>
                <option value="low">low</option>
                <option value="mid">mid</option>
                <option value="high">high</option>
                <option value="urgent">urgent</option>
              </select>
            </label>
            <label htmlFor="archived-tag-filter">Tag
              <input id="archived-tag-filter" value={filterDraft.tag} onChange={(event) => setFilterDraft((current) => ({ ...current, tag: event.target.value }))} placeholder="front-end" />
            </label>
            <label htmlFor="archived-project-filter">Project ID
              <input id="archived-project-filter" value={filterDraft.projectId} onChange={(event) => setFilterDraft((current) => ({ ...current, projectId: event.target.value }))} placeholder="proj_123" />
            </label>
            <label htmlFor="archived-assignee-filter">Assignee ID
              <input id="archived-assignee-filter" value={filterDraft.assigneeUserId} onChange={(event) => setFilterDraft((current) => ({ ...current, assigneeUserId: event.target.value }))} placeholder="usr_dev_001" />
            </label>
            <label htmlFor="archived-reporter-filter">Reporter ID
              <input id="archived-reporter-filter" value={filterDraft.reporterUserId} onChange={(event) => setFilterDraft((current) => ({ ...current, reporterUserId: event.target.value }))} placeholder="usr_dev_001" />
            </label>
            <div className="server-filter-actions">
              <button type="button" onClick={submitServerFilters}>{t('common.applyFilters', 'Apply Filters')}</button>
              <button type="button" className="tiny-action" onClick={resetServerFilters}>{t('common.resetFilters', 'Reset Filters')}</button>
            </div>
          </div>
        </CollapsibleFilters>
        <ExportControls token={token} userRole={userRole} tickets={quickFilteredTickets} viewName="archived ticket" />
      </form>
       {quickFilter === 'closed-this-week' ? <p className="subtitle">{t('pages.archived.closedThisWeekHelp', 'Closed This Week filters the current page because the server does not expose a date-range filter.')}</p> : null}
       {quickFilter === 'urgent' ? <p className="subtitle">{t('pages.archived.urgentHelp', 'Urgent (urgent severity or P0) filters the current page because the server does not expose an OR filter.')}</p> : null}

       {!loading && !error && quickFilteredTickets.length === 0 ? <p className="dashboard-empty">{t('pages.archived.empty', 'No archived tickets yet.')}</p> : null}

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
           title={t('pages.archived.reportsTitle', 'Archived Ticket Reports')}
          showReportTabs
          onAddComment={report.addComment}
          onClose={report.closeReport}
        />
      ) : null}

      {reopenTicket ? (
        <ReopenTicketPanel
          ticket={reopenTicket}
          submitting={reopenSubmitting}
          error={reopenError}
          conflict={reopenConflict?.conflict}
          latestTicket={reopenConflict?.latestTicket}
          conflictRefreshError={reopenConflict?.refreshError}
          onConflictReview={(fields) => setReopenConflict((current) => current ? { ...current, conflict: { ...current.conflict, changedFields: current.conflict.changedFields.filter((field) => !fields.includes(field)) } } : current)}
          onSubmit={submitReopen}
          onClose={() => {
            setReopenTicket(null);
            setReopenSubmitting(false);
            setReopenError('');
            setReopenConflict(null);
          }}
        />
      ) : null}
    </section>
  );
}
