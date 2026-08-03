import React from 'react';
import CollapsibleFilters from './CollapsibleFilters';
import ExportControls from './ExportControls';
import { useI18n } from '../i18n';

export default function ActiveTicketFilters({
  token,
  userRole,
  searchText,
  onSearchTextChange,
  onSearch,
  quickFilter,
  onQuickFilterChange,
  canAllocate,
  filterDraft,
  onFilterDraftChange,
  onApplyFilters,
  onResetFilters,
  visibleTickets,
  onBulkAssign,
  activeFilterCount = 0
}) {
  const { t } = useI18n();
  return (
    <form className="ticket-tools" onSubmit={onSearch}>
      <label htmlFor="ticketSearch">{t('tickets.filters.search', 'Search tickets')}</label>
      <div className="ticket-search-row">
        <input id="ticketSearch" value={searchText} onChange={(event) => onSearchTextChange(event.target.value)} placeholder={t('tickets.filters.searchPlaceholder', 'Title, report, project, tag, priority...')} />
        <button type="submit">{t('common.search', 'Search')}</button>
      </div>
      <CollapsibleFilters id="active-ticket-filters" activeCount={activeFilterCount}>
        <div className="filter-row">
          <button type="button" aria-pressed={quickFilter === 'all'} className={`filter-button ${quickFilter === 'all' ? 'active' : ''}`} onClick={() => onQuickFilterChange('all')}>{t('common.all', 'All')}</button>
          <button type="button" aria-pressed={quickFilter === 'urgent'} className={`filter-button ${quickFilter === 'urgent' ? 'active' : ''}`} onClick={() => onQuickFilterChange('urgent')}>{t('tickets.filters.urgent', 'Urgent')}</button>
          <button type="button" aria-pressed={quickFilter === 'recently-updated'} className={`filter-button ${quickFilter === 'recently-updated' ? 'active' : ''}`} onClick={() => onQuickFilterChange('recently-updated')}>{t('tickets.filters.recentlyUpdated', 'Recently Updated')}</button>
          {canAllocate ? <button type="button" aria-pressed={quickFilter === 'unassigned'} className={`filter-button ${quickFilter === 'unassigned' ? 'active' : ''}`} onClick={() => onQuickFilterChange('unassigned')}>{t('tickets.filters.unassigned', 'Unassigned')}</button> : null}
        </div>
        <div className="server-filter-grid" aria-label={t('tickets.filters.server', 'Server ticket filters')}>
          <label htmlFor="ticket-priority-filter">{t('tickets.columns.priority', 'Priority')}
            <select id="ticket-priority-filter" value={filterDraft.priority} onChange={(event) => onFilterDraftChange((current) => ({ ...current, priority: event.target.value }))}>
              <option value="">{t('common.any', 'Any')}</option>
              <option value="p0">P0</option>
              <option value="p1">P1</option>
              <option value="p2">P2</option>
              <option value="p3">P3</option>
            </select>
          </label>
          <label htmlFor="ticket-severity-filter">{t('tickets.columns.severity', 'Severity')}
            <select id="ticket-severity-filter" value={filterDraft.severity} onChange={(event) => onFilterDraftChange((current) => ({ ...current, severity: event.target.value }))}>
              <option value="">{t('common.any', 'Any')}</option>
              <option value="low">low</option>
              <option value="mid">mid</option>
              <option value="high">high</option>
              <option value="urgent">urgent</option>
            </select>
          </label>
          <label htmlFor="ticket-tag-filter">{t('tickets.filters.tag', 'Tag')}
            <input id="ticket-tag-filter" value={filterDraft.tag} onChange={(event) => onFilterDraftChange((current) => ({ ...current, tag: event.target.value }))} placeholder="front-end" />
          </label>
          <label htmlFor="ticket-project-filter">{t('tickets.filters.projectId', 'Project ID')}
            <input id="ticket-project-filter" value={filterDraft.projectId} onChange={(event) => onFilterDraftChange((current) => ({ ...current, projectId: event.target.value }))} placeholder="proj_123" />
          </label>
          <label htmlFor="ticket-assignee-filter">{t('tickets.filters.assigneeId', 'Assignee ID')}
            <input id="ticket-assignee-filter" value={filterDraft.assigneeUserId} onChange={(event) => onFilterDraftChange((current) => ({ ...current, assigneeUserId: event.target.value }))} placeholder="usr_dev_001" />
          </label>
          <label htmlFor="ticket-reporter-filter">{t('tickets.filters.reporterId', 'Reporter ID')}
            <input id="ticket-reporter-filter" value={filterDraft.reporterUserId} onChange={(event) => onFilterDraftChange((current) => ({ ...current, reporterUserId: event.target.value }))} placeholder="usr_dev_001" />
          </label>
          <div className="server-filter-actions">
            <button type="button" onClick={onApplyFilters}>{t('tickets.filters.apply', 'Apply Filters')}</button>
            <button type="button" className="tiny-action" onClick={onResetFilters}>{t('tickets.filters.reset', 'Reset Filters')}</button>
          </div>
        </div>
        {canAllocate ? (
          <div className="bulk-tools">
            <p>{t('tickets.filters.bulkScope', 'Bulk action scope: {{count}} currently visible tickets.', { count: visibleTickets.length })}</p>
            <button type="button" onClick={onBulkAssign} disabled={visibleTickets.length === 0}>{t('tickets.actions.bulkAssignVisible', 'Bulk Assign Visible Tickets')}</button>
          </div>
        ) : null}
      </CollapsibleFilters>
      <ExportControls token={token} userRole={userRole} tickets={visibleTickets} viewName="active ticket" />
    </form>
  );
}
