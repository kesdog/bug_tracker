import React, { useEffect, useMemo, useState } from 'react';
import { allocateBug, bulkAllocateBugs, cancelBug, closeBug, fetchBugPage, fetchAssignableUsers, fetchBugById, updateBugMetadata, updateBugReport, updateInitialBugReport } from '../api/bugs';
import ActiveTicketFilters from '../components/ActiveTicketFilters';
import TicketTable, { getStoredTicketPageSize } from '../components/TicketTable';
import ViewTicketPanels from '../components/ViewTicketPanels';
import CancelTicketDialog from '../components/CancelTicketDialog';
import useTicketReport from '../hooks/useTicketReport';
import { ACTION_EDIT_INITIAL, ACTION_SOLUTION, EMPTY_FILTERS, buildActiveTicketColumns, buildTicketMenuItems, hasSolutionReport } from '../view_tickets_config';
import { bulkConflictsFromError, bulkConflictsFromResult, recoverTicketConflict } from '../concurrency';
import { writeAppUrlState } from '../url_state';
import { filterTicketsByQuickFilter } from '../viewTicketsHelpers';

export default function ViewTicketsPage({ token, userRole, userType = 'human', currentUserId = '', initialFilters = EMPTY_FILTERS, initialSearch = '', initialQuickFilter = 'all', initialTicketId = '' }) {
  const [tickets, setTickets] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [reloadKey, setReloadKey] = useState(0);
  const report = useTicketReport(token);
  const [allocateTicket, setAllocateTicket] = useState(null);
  const [assignees, setAssignees] = useState([]);
  const [selectedAssignee, setSelectedAssignee] = useState('');
  const [allocateLoading, setAllocateLoading] = useState(false);
  const [allocateSubmitting, setAllocateSubmitting] = useState(false);
  const [allocateError, setAllocateError] = useState('');
  const [actionTicket, setActionTicket] = useState(null);
  const [actionType, setActionType] = useState('');
  const [actionSubmitting, setActionSubmitting] = useState(false);
  const [actionError, setActionError] = useState('');
  const [closeTicket, setCloseTicket] = useState(null);
  const [closeSubmitting, setCloseSubmitting] = useState(false);
  const [closeError, setCloseError] = useState('');
  const [searchText, setSearchText] = useState(initialSearch);
  const [searchTerm, setSearchTerm] = useState(initialSearch);
  const [quickFilter, setQuickFilter] = useState(initialQuickFilter);
  const [filterDraft, setFilterDraft] = useState({ ...EMPTY_FILTERS, ...initialFilters });
  const [serverFilters, setServerFilters] = useState({ ...EMPTY_FILTERS, ...initialFilters });
  const [metadataTicket, setMetadataTicket] = useState(null);
  const [metadataSubmitting, setMetadataSubmitting] = useState(false);
  const [metadataError, setMetadataError] = useState('');
  const [bulkPanelOpen, setBulkPanelOpen] = useState(false);
  const [bulkAssignee, setBulkAssignee] = useState('');
  const [bulkSubmitting, setBulkSubmitting] = useState(false);
  const [bulkError, setBulkError] = useState('');
  const [bulkLoading, setBulkLoading] = useState(false);
  const [bulkTargetIds, setBulkTargetIds] = useState([]);
  const [bulkLatestVersions, setBulkLatestVersions] = useState({});
  const [allocateConflict, setAllocateConflict] = useState(null);
  const [actionConflict, setActionConflict] = useState(null);
  const [closeConflict, setCloseConflict] = useState(null);
  const [cancelTicket, setCancelTicket] = useState(null);
  const [cancelSubmitting, setCancelSubmitting] = useState(false);
  const [cancelError, setCancelError] = useState('');
  const [metadataConflict, setMetadataConflict] = useState(null);
  const [bulkConflicts, setBulkConflicts] = useState([]);
  const [paginationModel, setPaginationModel] = useState(() => ({ page: 0, pageSize: getStoredTicketPageSize(currentUserId) }));
  const [totalCount, setTotalCount] = useState(0);
  const cursors = React.useRef([null]);

  const canAllocate = userRole === 'senior' || userRole === 'admin';
  const canModifyFromView = userRole === 'senior' || userRole === 'admin';
  const canEditMetadata = userRole === 'senior' || userRole === 'admin';
  const canCloseFromView = userRole === 'senior' || userRole === 'admin';

  useEffect(() => {
    if (initialTicketId) report.openReport({ id: initialTicketId });
  }, [initialTicketId, report.openReport]);

  async function openAllocate(ticket) {
    if (!canAllocate) {
      return;
    }

    setAllocateTicket(ticket);
    setSelectedAssignee(ticket.assigneeUserId || '');
    setAllocateError('');
    setAllocateConflict(null);
    setAllocateLoading(true);

    try {
      const users = await fetchAssignableUsers(token);
      setAssignees(Array.isArray(users) ? users : []);
    } catch (err) {
      setAssignees([]);
      setAllocateError(err.message || 'Unable to load users.');
    } finally {
      setAllocateLoading(false);
    }
  }

  async function ensureAssigneesLoaded() {
    if (!canAllocate || assignees.length > 0) {
      return;
    }

    setBulkLoading(true);
    setBulkError('');
    try {
      const users = await fetchAssignableUsers(token);
      setAssignees(Array.isArray(users) ? users : []);
    } catch (err) {
      setAssignees([]);
      setBulkError(err.message || 'Unable to load users.');
    } finally {
      setBulkLoading(false);
    }
  }

  async function handleAllocate(event) {
    event.preventDefault();

    if (!allocateTicket || !selectedAssignee) {
      return;
    }

    setAllocateSubmitting(true);
    setAllocateError('');

    try {
      await allocateBug(token, allocateTicket.id, selectedAssignee, allocateConflict?.latestTicket?.version || allocateTicket.version);
      setAllocateTicket(null);
      setSelectedAssignee('');
      setReloadKey((value) => value + 1);
    } catch (err) {
      const recovered = await recoverTicketConflict(err, token);
      if (recovered) {
        setAllocateConflict(recovered);
      } else {
        setAllocateError(err.message || 'Unable to allocate ticket.');
      }
    } finally {
      setAllocateSubmitting(false);
    }
  }

  async function openReportAction(ticket, nextActionType = ACTION_SOLUTION) {
    if (!canModifyFromView) {
      return;
    }

    report.closeReport();
    setActionError('');
    setActionConflict(null);
    setActionType(nextActionType);
    try {
      const fullTicket = await fetchBugById(token, ticket.id);
      setActionTicket(fullTicket);
    } catch (err) {
      setError(err.message || 'Unable to load ticket details.');
    }
  }

  async function openModifyFromReport(ticket) {
    await openReportAction(ticket, ACTION_SOLUTION);
  }

  async function openCloseFromView(ticket) {
    if (!canCloseFromView) {
      return;
    }

    setCloseError('');
    setCloseConflict(null);
    try {
      const fullTicket = await fetchBugById(token, ticket.id);
      if (hasSolutionReport(fullTicket) || (fullTicket.resolutionReportImages || []).length > 0) {
        setCloseTicket(fullTicket);
      } else {
        setCancelTicket(fullTicket);
      }
    } catch (err) {
      setError(err.message || 'Unable to load ticket details.');
    }
  }

  async function cancelTicketWithoutSolution(reason) {
    if (!cancelTicket) return;
    setCancelSubmitting(true);
    setCancelError('');
    try {
      await cancelBug(token, cancelTicket.id, reason, cancelTicket.version);
      setTickets((current) => current.filter((ticket) => ticket.id !== cancelTicket.id));
      setCancelTicket(null);
      setReloadKey((value) => value + 1);
    } catch (err) {
      setCancelError(err.message || 'Unable to cancel ticket.');
    } finally {
      setCancelSubmitting(false);
    }
  }

  async function submitModifyForm({ text, images }) {
    if (!actionTicket) {
      return;
    }

    setActionSubmitting(true);
    setActionError('');
    try {
      if (actionType === ACTION_EDIT_INITIAL) {
        await updateInitialBugReport(token, actionTicket.id, text, images, actionConflict?.latestTicket?.version || actionTicket.version);
      } else {
        await updateBugReport(token, actionTicket.id, text, images, actionConflict?.latestTicket?.version || actionTicket.version);
      }
      setActionTicket(null);
      setActionType('');
      setReloadKey((value) => value + 1);
    } catch (err) {
      const recovered = await recoverTicketConflict(err, token);
      if (recovered) {
        setActionConflict(recovered);
      } else {
        setActionError(err.message || 'Unable to modify bug report.');
      }
    } finally {
      setActionSubmitting(false);
    }
  }

  async function submitCloseForm({ text, images }) {
    if (!closeTicket) {
      return;
    }

    setCloseSubmitting(true);
    setCloseError('');
    try {
      await closeBug(token, closeTicket.id, text, images, closeConflict?.latestTicket?.version || closeTicket.version);
      setTickets((current) => current.filter((ticket) => ticket.id !== closeTicket.id));
      setCloseTicket(null);
      setReloadKey((value) => value + 1);
    } catch (err) {
      const recovered = await recoverTicketConflict(err, token);
      if (recovered) {
        setCloseConflict(recovered);
      } else {
        setCloseError(err.message || 'Unable to close bug.');
      }
    } finally {
      setCloseSubmitting(false);
    }
  }

  async function submitMetadataForm(metadata) {
    if (!metadataTicket) {
      return;
    }

    setMetadataSubmitting(true);
    setMetadataError('');
    try {
      await updateBugMetadata(token, metadataTicket.id, metadata, metadataConflict?.latestTicket?.version || metadataTicket.version);
      setMetadataTicket(null);
      setReloadKey((value) => value + 1);
    } catch (err) {
      const recovered = await recoverTicketConflict(err, token);
      if (recovered) {
        setMetadataConflict(recovered);
      } else {
        setMetadataError(err.message || 'Unable to update ticket metadata.');
      }
    } finally {
      setMetadataSubmitting(false);
    }
  }

  async function openBulkAssign() {
    setBulkPanelOpen(true);
    setBulkAssignee('');
    setBulkConflicts([]);
    setBulkTargetIds(quickFilteredTickets.map((ticket) => ticket.id).filter(Boolean));
    setBulkLatestVersions({});
    await ensureAssigneesLoaded();
  }

  async function refreshBulkFailures(failedIds, conflicts) {
    const latest = await Promise.all(failedIds.map(async (ticketId) => {
      try { return await fetchBugById(token, ticketId); } catch { return null; }
    }));
    const byId = new Map(latest.filter(Boolean).map((ticket) => [ticket.id, ticket]));
    setBulkLatestVersions(Object.fromEntries(latest.filter(Boolean).map((ticket) => [ticket.id, ticket.version])));
    setBulkConflicts(conflicts.map((conflict) => ({ ...conflict, latestTicket: byId.get(conflict.ticketId) || null })));
    return failedIds.filter((ticketId) => !byId.has(ticketId));
  }

  async function submitBulkAssign(event) {
    event.preventDefault();
    const pendingIds = bulkTargetIds.filter(Boolean);
    if (!bulkAssignee || pendingIds.length === 0) {
      return;
    }

    setBulkSubmitting(true);
    setBulkError('');
    try {
      const ticketsById = new Map(tickets.map((ticket) => [ticket.id, ticket]));
      const result = await bulkAllocateBugs(token, pendingIds.map((ticketId) => ({ ticketId, expectedVersion: bulkLatestVersions[ticketId] || ticketsById.get(ticketId)?.version })), bulkAssignee);
      const updatedTickets = Array.isArray(result?.updated) ? result.updated : [];
      if (updatedTickets.length > 0) {
        const updatedById = new Map(updatedTickets.filter((ticket) => ticket?.id).map((ticket) => [ticket.id, ticket]));
        setTickets((current) => current.map((ticket) => updatedById.has(ticket.id) ? { ...ticket, ...updatedById.get(ticket.id) } : ticket));
      }

      const failures = Array.isArray(result?.failed) ? result.failed : [];
      const conflicts = bulkConflictsFromResult(result);
      if (failures.length > 0) {
        const failedIds = failures.map((failure) => failure.ticketId).filter(Boolean);
        setBulkTargetIds(failedIds);
        const unrefreshedIds = await refreshBulkFailures(failedIds, conflicts);
        const otherFailures = failures.filter((failure) => !failure.conflict && failure.error !== 'ticket_version_conflict');
        const messages = otherFailures.map((failure) => `${failure.ticketId}: ${failure.error || 'assignment failed'}`);
        if (unrefreshedIds.length > 0) messages.push(`Latest versions could not be loaded for: ${unrefreshedIds.join(', ')}`);
        setBulkError(messages.join('; '));
        return;
      }
      setBulkPanelOpen(false);
      setBulkAssignee('');
      setBulkTargetIds([]);
      setBulkLatestVersions({});
      setReloadKey((value) => value + 1);
    } catch (err) {
      const conflicts = bulkConflictsFromError(err);
      if (conflicts.length > 0) {
        const failedIds = conflicts.map((conflict) => conflict.ticketId).filter(Boolean);
        setBulkTargetIds(failedIds);
        const unrefreshedIds = await refreshBulkFailures(failedIds, conflicts);
        setBulkError(unrefreshedIds.length > 0 ? `Latest versions could not be loaded for: ${unrefreshedIds.join(', ')}` : '');
      } else {
        setBulkError(err.message || 'Unable to bulk assign visible tickets.');
      }
    } finally {
      setBulkSubmitting(false);
    }
  }

  const columns = buildActiveTicketColumns();
  const ticketMenuItems = buildTicketMenuItems({ canAllocate, canModifyFromView, canEditMetadata, canCloseFromView, openAllocate, openReport: report.openReport, openReportAction, setMetadataError, setMetadataTicket, openCloseFromView });

  useEffect(() => {
    let isActive = true;

    async function loadTickets() {
      setLoading(true);
      setError('');
      try {
          const quickServerFilters = quickFilter === 'unassigned' ? { assigneeUserId: 'unassigned' } : {};
          const nextPage = await fetchBugPage(token, {
            status: 'active', limit: paginationModel.pageSize, cursor: cursors.current[paginationModel.page] || '', search: searchTerm,
            filters: { ...serverFilters, ...quickServerFilters },
            sort: 'created_at_desc'
          });
        if (isActive) {
          setTickets(nextPage.items);
          setTotalCount(nextPage.totalCount);
          cursors.current[paginationModel.page + 1] = nextPage.nextCursor;
        }
      } catch (err) {
        if (isActive) {
          setTickets([]);
          setError(err.message || 'Unable to load tickets.');
        }
      } finally {
        if (isActive) {
          setLoading(false);
        }
      }
    }

    loadTickets();

    return () => {
      isActive = false;
    };
  }, [token, reloadKey, searchTerm, serverFilters, quickFilter, paginationModel]);

  const quickFilteredTickets = useMemo(() => filterTicketsByQuickFilter(tickets, quickFilter), [quickFilter, tickets]);

  function submitSearch(event) {
    event.preventDefault();
    setSearchTerm(searchText.trim());
    writeAppUrlState({ view: 'tickets', search: searchText.trim(), quick: quickFilter, filters: serverFilters, ticket: '' });
    resetPage();
  }

  function submitServerFilters(event) {
    event.preventDefault();
    setServerFilters({ ...filterDraft });
    writeAppUrlState({ view: 'tickets', search: searchTerm, quick: quickFilter, filters: filterDraft, ticket: '' });
    resetPage();
  }

  function resetServerFilters() {
    setFilterDraft(EMPTY_FILTERS);
    setServerFilters(EMPTY_FILTERS);
    writeAppUrlState({ view: 'tickets', search: searchTerm, quick: quickFilter, filters: EMPTY_FILTERS, ticket: '' });
    resetPage();
  }

  function resetPage() {
    cursors.current = [null];
    setPaginationModel((current) => ({ ...current, page: 0 }));
  }

  function changeQuickFilter(value) {
    setQuickFilter(value);
    writeAppUrlState({ view: 'tickets', search: searchTerm, quick: value, filters: serverFilters, ticket: '' });
    resetPage();
  }

  return (
    <section className="dashboard">
      <h2>View Tickets</h2>
      <p className="subtitle">Browse active bugs across your visible projects and open detailed reports.</p>

      {error ? (
        <p role="alert" className="error-text">
          {error}
        </p>
      ) : null}

      {loading ? <div className="spinner" aria-label="loading tickets" /> : null}

      <ActiveTicketFilters
           token={token}
           userType={userType}
        userRole={userRole}
        searchText={searchText}
        onSearchTextChange={setSearchText}
        onSearch={submitSearch}
        quickFilter={quickFilter}
        onQuickFilterChange={changeQuickFilter}
        canAllocate={canAllocate}
        filterDraft={filterDraft}
        onFilterDraftChange={setFilterDraft}
        onApplyFilters={submitServerFilters}
        onResetFilters={resetServerFilters}
        visibleTickets={quickFilteredTickets}
        onBulkAssign={openBulkAssign}
        activeFilterCount={Number(quickFilter !== 'all') + Object.values(serverFilters).filter(Boolean).length}
      />
      {quickFilter === 'recently-updated' ? <p className="subtitle">Recently Updated sorts this current page only.</p> : null}
      {quickFilter === 'urgent' ? <p className="subtitle">Urgent (urgent severity or P0) filters the current page because the server does not expose an OR filter.</p> : null}

      {!loading && !error && quickFilteredTickets.length === 0 ? <p className="dashboard-empty">No active tickets in this view.</p> : null}

      {!error && (totalCount > 0 || quickFilteredTickets.length > 0) ? (
        <TicketTable tickets={quickFilteredTickets} columns={columns} rowMenuItems={ticketMenuItems} loading={loading} currentUserId={currentUserId} rowCount={totalCount} paginationModel={paginationModel} onPaginationModelChange={(next) => { if (next.pageSize !== paginationModel.pageSize) cursors.current = [null]; setPaginationModel(next.pageSize !== paginationModel.pageSize ? { ...next, page: 0 } : next); }} />
      ) : null}

      <ViewTicketPanels
        report={report} token={token} canModifyFromView={canModifyFromView} openModifyFromReport={openModifyFromReport}
        actionTicket={actionTicket} actionType={actionType} actionSubmitting={actionSubmitting} actionError={actionError} actionConflict={actionConflict} setActionConflict={setActionConflict} submitModifyForm={submitModifyForm}
        closeActionPanel={() => { setActionTicket(null); setActionType(''); setActionSubmitting(false); setActionError(''); setActionConflict(null); }}
        closeTicket={closeTicket} closeSubmitting={closeSubmitting} closeError={closeError} closeConflict={closeConflict} setCloseConflict={setCloseConflict} submitCloseForm={submitCloseForm}
        closeClosePanel={() => { setCloseTicket(null); setCloseSubmitting(false); setCloseError(''); setCloseConflict(null); }}
        allocateTicket={allocateTicket} assignees={assignees} selectedAssignee={selectedAssignee} allocateLoading={allocateLoading} allocateSubmitting={allocateSubmitting} allocateError={allocateError} allocateConflict={allocateConflict} setAllocateConflict={setAllocateConflict} setSelectedAssignee={setSelectedAssignee} handleAllocate={handleAllocate}
        closeAllocatePanel={() => { setAllocateTicket(null); setSelectedAssignee(''); setAllocateLoading(false); setAllocateSubmitting(false); setAllocateError(''); setAllocateConflict(null); }}
        metadataTicket={metadataTicket} metadataSubmitting={metadataSubmitting} metadataError={metadataError} metadataConflict={metadataConflict} setMetadataConflict={setMetadataConflict} submitMetadataForm={submitMetadataForm}
        closeMetadataPanel={() => { setMetadataTicket(null); setMetadataSubmitting(false); setMetadataError(''); setMetadataConflict(null); }}
        bulkPanelOpen={bulkPanelOpen} quickFilteredTickets={quickFilteredTickets} bulkTargetIds={bulkTargetIds} bulkAssignee={bulkAssignee} bulkLoading={bulkLoading} bulkSubmitting={bulkSubmitting} bulkError={bulkError} bulkConflicts={bulkConflicts} setBulkConflicts={setBulkConflicts} bulkLatestVersions={bulkLatestVersions} setBulkAssignee={setBulkAssignee} submitBulkAssign={submitBulkAssign}
        closeBulkPanel={() => { setBulkPanelOpen(false); setBulkAssignee(''); setBulkSubmitting(false); setBulkError(''); setBulkConflicts([]); setBulkTargetIds([]); setBulkLatestVersions({}); }}
      />
      <CancelTicketDialog ticket={cancelTicket} submitting={cancelSubmitting} error={cancelError} onCancel={cancelTicketWithoutSolution} onClose={() => { setCancelTicket(null); setCancelError(''); }} />
    </section>
  );
}
