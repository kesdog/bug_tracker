import React, { useEffect, useMemo, useRef, useState } from 'react';
import Box from '@mui/material/Box';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import { alpha } from '@mui/material/styles';
import { DataGrid } from '@mui/x-data-grid';
import { PriorityChip, SeverityChip, StatusChip } from './MuiPrimitives';
import { formatActiveSince, formatTicketDate, getProjectName, getTicketSortValue, TICKET_FIELD_ACCESSORS } from '../table_utils';
import { useI18n } from '../i18n';

const DATE_FIELDS = new Set(['createdAt', 'updatedAt', 'closeDate', 'assignedAt']);
const ORDERED_FIELDS = new Set(['severity', 'status', 'priority']);
export const TICKET_PAGE_SIZE_STORAGE_KEY = 'bug-tracker.ticket-page-size.v1';
export const TICKET_PAGE_SIZE_OPTIONS = [10, 25, 50, 100];

export function getStoredTicketPageSize(userId = '') {
  if (typeof localStorage === 'undefined') return 25;
  const value = Number(localStorage.getItem(`${TICKET_PAGE_SIZE_STORAGE_KEY}:${userId || 'anonymous'}`));
  return TICKET_PAGE_SIZE_OPTIONS.includes(value) ? value : 25;
}

const FIELD_WIDTHS = {
  issueTitle: { minWidth: 220, flex: 1.5 },
  description: { minWidth: 220, flex: 1.4 },
  status: { minWidth: 130, flex: 0.75 },
  reporterUserId: { minWidth: 160, flex: 1 },
  assigneeUserId: { minWidth: 160, flex: 1 },
  createdAt: { minWidth: 180, flex: 1 },
  assignedAt: { minWidth: 210, flex: 1.05 },
  updatedAt: { minWidth: 180, flex: 1 },
  closeDate: { minWidth: 180, flex: 1 },
  projectName: { minWidth: 170, flex: 1 },
  severity: { minWidth: 130, flex: 0.75 },
  priority: { minWidth: 120, flex: 0.65 },
  resolvedBy: { minWidth: 160, flex: 1 },
  resolvedReport: { minWidth: 190, flex: 1 },
  options: { minWidth: 140, flex: 0.65 }
};

function defaultRenderCell(ticket, column) {
  if (column.render) {
    return column.render(ticket);
  }

  if (column.key === 'assignedAt') {
    return formatActiveSince(ticket);
  }

  if (DATE_FIELDS.has(column.key)) {
    return formatTicketDate(ticket[column.key]);
  }

  if (column.key === 'projectName') {
    return getProjectName(ticket);
  }

  if (column.key === 'status') {
    return <StatusChip value={ticket.status} />;
  }

  if (column.key === 'severity') {
    return <SeverityChip value={ticket.severity} />;
  }

  if (column.key === 'priority') {
    return <PriorityChip value={ticket.priority} />;
  }

  return ticket[column.key] || '-';
}

function getCellValue(ticket, key) {
  if (ORDERED_FIELDS.has(key)) {
    return getTicketSortValue(ticket, key);
  }

  const accessor = TICKET_FIELD_ACCESSORS[key];
  if (accessor) {
    return accessor(ticket);
  }

  return ticket[key] || '';
}

function getGridHeight(rowCount, isCompact) {
  if (isCompact) {
    return Math.min(420, Math.max(320, 142 + rowCount * 56));
  }

  return Math.min(680, Math.max(420, 150 + rowCount * 58));
}

export default function TicketTable({
  tickets,
  columns,
  defaultSort,
  rowMenuItems = [],
  wrapClassName = '',
  loading = false,
  paginationModel,
  onPaginationModelChange,
  rowCount,
  sortModel,
  onSortModelChange,
  filterModel,
  onFilterModelChange,
  currentUserId = ''
}) {
  const { t } = useI18n();
  const [menuState, setMenuState] = useState(null);
  const hasRowMenu = rowMenuItems.length > 0;
  const isCompact = wrapClassName.includes('dashboard-ticket-scroll');
  const rowCountRef = useRef(Number.isFinite(rowCount) ? rowCount : 0);
  if (Number.isFinite(rowCount)) rowCountRef.current = rowCount;
  const isServerPaginated = Boolean(paginationModel && onPaginationModelChange);

  const rows = useMemo(() => {
    return tickets.map((ticket, index) => ({
      ...ticket,
      id: ticket.id || `ticket-row-${index}`
    }));
  }, [tickets]);

  const gridColumns = useMemo(() => {
    // Tags and report actions remain available in ticket details and row actions,
    // not as dense default table columns.
    return columns.filter((column) => column.key !== 'tags' && column.key !== 'viewReports').map((column) => {
      const width = FIELD_WIDTHS[column.key] || { minWidth: 140, flex: 1 };

      return {
        field: column.key,
        headerName: column.label,
        sortable: Boolean(column.sortable),
        sortingOrder: ['asc', 'desc'],
        minWidth: width.minWidth,
        flex: width.flex,
        align: column.key === 'issueTitle' ? 'left' : 'center',
        headerAlign: 'center',
        valueGetter: (_value, row) => getCellValue(row, column.key),
        renderCell: (params) => defaultRenderCell(params.row, column)
      };
    });
  }, [columns]);

  const initialState = useMemo(() => ({
    columns: {
      columnVisibilityModel: {
        priority: false
      }
    },
    sorting: {
      sortModel: defaultSort ? [{ field: defaultSort.key, sort: defaultSort.direction }] : []
    }
  }), [defaultSort]);

  function openRowMenu(ticket, clientX, clientY) {
    if (!hasRowMenu || !ticket) {
      return;
    }

    setMenuState({
      ticket,
      x: Math.max(clientX, 8),
      y: Math.max(clientY, 8)
    });
  }

  function closeMenu() {
    setMenuState(null);
  }

  function onMenuItemSelect(item, ticket) {
    if (item.onSelect) {
      item.onSelect(ticket);
    }
    closeMenu();
  }

  useEffect(() => {
    if (!isServerPaginated || !TICKET_PAGE_SIZE_OPTIONS.includes(paginationModel.pageSize)) return;
    localStorage.setItem(`${TICKET_PAGE_SIZE_STORAGE_KEY}:${currentUserId || 'anonymous'}`, String(paginationModel.pageSize));
  }, [currentUserId, isServerPaginated, paginationModel?.pageSize]);

  const wrapperClass = ['bug-table-wrap', wrapClassName].filter(Boolean).join(' ');

  return (
    <>
      {hasRowMenu ? <Box component="p" className="table-action-hint">{t('tickets.table.actionHint', 'Click or tap a ticket for options.')}</Box> : null}
      <Box
        className={wrapperClass}
        sx={{ width: '100%', minHeight: isCompact ? 320 : 420, height: getGridHeight(rows.length, isCompact) }}
      >
        <DataGrid
        rows={rows}
        columns={gridColumns}
        loading={loading}
        initialState={initialState}
        {...(isServerPaginated ? {
          paginationMode: 'server',
          paginationModel,
          onPaginationModelChange,
          rowCount: rowCountRef.current,
          paginationMeta: { hasNextPage: rowCountRef.current > ((paginationModel.page + 1) * paginationModel.pageSize) },
          pageSizeOptions: TICKET_PAGE_SIZE_OPTIONS
        } : {})}
        {...(onSortModelChange ? { sortingMode: 'server', sortModel: sortModel || [], onSortModelChange } : {})}
        {...(onFilterModelChange ? { filterMode: 'server', filterModel: filterModel || { items: [] }, onFilterModelChange } : {})}
        disableRowSelectionOnClick
        disableVirtualization={import.meta.env.MODE === 'test'}
        hideFooter={!isServerPaginated}
        showToolbar={false}
        disableColumnFilter
        slotProps={{
          columnsManagement: {
            autoFocusSearchField: false
          }
        }}
        aria-label={t('tickets.table.ariaLabel', 'Ticket table')}
        onRowClick={(params, event) => {
          if (!hasRowMenu || event.target?.closest?.('button, a, input, select, textarea')) {
            return;
          }

          openRowMenu(params.row, event.clientX, event.clientY);
        }}
        onCellKeyDown={(params, event) => {
          if (!hasRowMenu || (event.key !== 'Enter' && event.key !== ' ') || event.target?.closest?.('button, a, input, select, textarea')) {
            return;
          }

          event.preventDefault();
          event.defaultMuiPrevented = true;
          const rect = event.currentTarget.getBoundingClientRect();
          openRowMenu(params.row, rect.left + rect.width / 2, rect.top + rect.height / 2);
        }}
        getRowClassName={(params) => (params.indexRelativeToCurrentPage % 2 === 0 ? 'ticket-row-even' : 'ticket-row-odd')}
        sx={(theme) => ({
          borderRadius: 3,
          bgcolor: 'background.paper',
          '--DataGrid-overlayHeight': '240px',
          '& .MuiDataGrid-row': {
            cursor: hasRowMenu ? 'pointer' : 'default'
          },
          '& .MuiDataGrid-row.ticket-row-odd': {
            bgcolor: alpha(theme.palette.primary.main, 0.045),
            ...theme.applyStyles('dark', {
              bgcolor: alpha(theme.palette.primary.main, 0.10)
            })
          },
          '& .MuiDataGrid-row.ticket-row-even': {
            bgcolor: alpha(theme.palette.background.paper, 0.64),
            ...theme.applyStyles('dark', {
              bgcolor: alpha(theme.palette.common.white, 0.015)
            })
          },
          '& .MuiDataGrid-row:hover, & .MuiDataGrid-row.ticket-row-odd:hover, & .MuiDataGrid-row.ticket-row-even:hover': {
            bgcolor: alpha(theme.palette.primary.main, 0.10),
            ...theme.applyStyles('dark', {
              bgcolor: alpha(theme.palette.primary.main, 0.18)
            })
          },
          '& .MuiDataGrid-row.Mui-selected, & .MuiDataGrid-row.Mui-selected:hover': {
            bgcolor: alpha(theme.palette.primary.main, 0.16),
            ...theme.applyStyles('dark', {
              bgcolor: alpha(theme.palette.primary.main, 0.26)
            })
          },
          '& .MuiDataGrid-cell': {
            alignItems: 'center'
          },
          '& .MuiDataGrid-columnHeaders': {
            bgcolor: theme.palette.background.paper,
            borderBottom: `1px solid ${theme.palette.divider}`,
            ...theme.applyStyles('dark', {
              bgcolor: '#101b2d',
              borderBottomColor: 'rgba(125, 211, 252, 0.32)'
            })
          },
          '& .MuiDataGrid-columnHeader': {
            bgcolor: theme.palette.background.paper,
            justifyContent: 'center',
            ...theme.applyStyles('dark', {
              bgcolor: '#101b2d'
            })
          },
          '& .MuiDataGrid-columnHeaderTitleContainer': {
            justifyContent: 'center'
          },
          '& .MuiDataGrid-columnHeaderTitle': {
            fontWeight: 800,
            letterSpacing: '0.03em',
            textTransform: 'uppercase'
          },
          '& .MuiDataGrid-columnHeader--sortable': {
            cursor: 'pointer'
          },
          '& .MuiDataGrid-columnHeader--sortable:focus-visible': {
            outline: `2px solid ${theme.palette.primary.main}`,
            outlineOffset: -2
          }
        })}
        />
      </Box>

      <Menu
        open={Boolean(menuState)}
        onClose={closeMenu}
        anchorReference="anchorPosition"
        anchorPosition={menuState ? { top: menuState.y, left: menuState.x } : undefined}
        slotProps={{ list: { 'aria-label': t('tickets.actions.menu', 'Ticket actions') } }}
      >
        {menuState ? rowMenuItems.filter((item) => !item.shouldShow || item.shouldShow(menuState.ticket)).map((item) => (
          <MenuItem key={`${menuState.ticket.id}-${item.key}`} disabled={item.disabled ? item.disabled(menuState.ticket) : false} onClick={() => onMenuItemSelect(item, menuState.ticket)}>
            {typeof item.label === 'function' ? item.label(menuState.ticket) : item.label}
          </MenuItem>
        )) : null}
      </Menu>
    </>
  );
}
