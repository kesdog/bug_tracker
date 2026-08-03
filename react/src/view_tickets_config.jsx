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

export function buildActiveTicketColumns() {
  return [
    { key: 'issueTitle', label: 'Bug', sortable: true, defaultDirection: 'asc' },
    { key: 'status', label: 'Status', sortable: true, defaultDirection: 'asc' },
    { key: 'reporterUserId', label: 'Reported By', sortable: true, defaultDirection: 'asc' },
    { key: 'assigneeUserId', label: 'Assignee', sortable: true, defaultDirection: 'asc' },
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
    }
  ];
}

export function buildTicketMenuItems({ canAllocate, canModifyFromView, canEditMetadata, canCloseFromView, openAllocate, openReport, openReportAction, setMetadataError, setMetadataTicket, openCloseFromView }) {
  return [
    ...(canAllocate
      ? [
          {
            key: 'allocate-to',
            label: 'Allocate To',
            onSelect: openAllocate
          }
        ]
      : []),
    {
      key: 'view-report',
      label: 'View Reports',
      onSelect: openReport
    },
    ...(canModifyFromView
      ? [
          {
            key: ACTION_EDIT_INITIAL,
            label: 'Edit Bug Report',
            onSelect: (ticket) => openReportAction(ticket, ACTION_EDIT_INITIAL)
          },
          {
            key: ACTION_SOLUTION,
            label: (ticket) => hasSolutionReport(ticket) ? 'Modify Solution Steps' : 'Create Solution',
            onSelect: (ticket) => openReportAction(ticket, ACTION_SOLUTION)
          }
        ]
      : []),
    ...(canEditMetadata
      ? [
          {
            key: 'edit-metadata',
            label: 'Edit Metadata',
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
            label: 'Close Bug',
            onSelect: openCloseFromView
          }
        ]
      : [])
  ];
}
