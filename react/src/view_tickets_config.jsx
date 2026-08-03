import React from 'react';
import { PriorityChip, SeverityChip } from './components/MuiPrimitives';
import { getProjectName } from './table_utils';

export const EMPTY_FILTERS = { priority: '', severity: '', tag: '', projectId: '', assigneeUserId: '', reporterUserId: '' };
export const ACTION_EDIT_INITIAL = 'edit-initial';
export const ACTION_SOLUTION = 'solution';

function renderPriority(ticket) {
  const priority = ticket.priority || 'p2';
  return <PriorityChip value={priority} />;
}

export function hasSolutionReport(ticket) {
  return Boolean((ticket?.postResolutionReport || ticket?.resolutionNotes || '').trim()) || (ticket?.resolutionReportImages || []).length > 0;
}

export function getEditableReportText(ticket) {
  return ticket?.postResolutionReport || ticket?.resolutionNotes || '';
}

export function getInitialReportText(ticket) {
  return ticket?.description || '';
}

const defaultT = (_key, fallback) => fallback;

export function buildActiveTicketColumns(t = defaultT) {
  return [
    { key: 'issueTitle', label: t('tickets.columns.bug', 'Bug'), sortable: true, defaultDirection: 'asc' },
    { key: 'status', label: t('tickets.columns.status', 'Status'), sortable: true, defaultDirection: 'asc' },
    { key: 'reporterUserId', label: t('tickets.columns.reportedBy', 'Reported By'), sortable: true, defaultDirection: 'asc' },
    { key: 'assigneeUserId', label: t('tickets.columns.assignee', 'Assignee'), sortable: true, defaultDirection: 'asc' },
    { key: 'assignedAt', label: t('tickets.columns.activeSince', 'Active Since'), sortable: true, defaultDirection: 'desc' },
    {
      key: 'projectName',
      label: t('tickets.columns.project', 'Project'),
      sortable: true,
      defaultDirection: 'asc',
      render: (ticket) => getProjectName(ticket)
    },
    {
      key: 'severity',
      label: t('tickets.columns.severity', 'Severity'),
      sortable: true,
      defaultDirection: 'desc',
      render: (ticket) => <SeverityChip value={ticket.severity} />
    },
    {
      key: 'priority',
      label: t('tickets.columns.priority', 'Priority'),
      sortable: true,
      defaultDirection: 'desc',
      render: renderPriority
    }
  ];
}

export function buildTicketMenuItems({ canAllocate, canModifyFromView, canEditMetadata, canCloseFromView, openAllocate, openReport, openReportAction, setMetadataError, setMetadataTicket, openCloseFromView, t = defaultT }) {
  return [
    ...(canAllocate
      ? [
          {
            key: 'allocate-to',
            label: t('tickets.actions.allocateTo', 'Allocate To'),
            onSelect: openAllocate
          }
        ]
      : []),
    {
      key: 'view-report',
      label: t('tickets.actions.viewReports', 'View Reports'),
      onSelect: openReport
    },
    ...(canModifyFromView
      ? [
          {
            key: ACTION_EDIT_INITIAL,
            label: t('tickets.actions.editBugReport', 'Edit Bug Report'),
            onSelect: (ticket) => openReportAction(ticket, ACTION_EDIT_INITIAL)
          },
          {
            key: ACTION_SOLUTION,
            label: (ticket) => hasSolutionReport(ticket) ? t('tickets.actions.modifySolutionSteps', 'Modify Solution Steps') : t('tickets.actions.createSolution', 'Create Solution'),
            onSelect: (ticket) => openReportAction(ticket, ACTION_SOLUTION)
          }
        ]
      : []),
    ...(canEditMetadata
      ? [
          {
            key: 'edit-metadata',
            label: t('tickets.actions.editMetadata', 'Edit Metadata'),
            onSelect: (ticket) => {
              setMetadataError('');
              setMetadataTicket(ticket);
            }
          }
        ]
      : []),
    ...(canCloseFromView
      ? [
          {
            key: 'close-bug',
            label: t('tickets.actions.closeBug', 'Close Bug'),
            onSelect: openCloseFromView
          }
        ]
      : [])
  ];
}
