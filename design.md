# Frontend Design Guide

This file documents the frontend structure, Material UI migration direction, and testing guidance for the React application.

## Stack

- React 19 with Vite 6.
- Frontend tests use Vitest 3, Testing Library, `@testing-library/user-event`, and `jsdom`.
- The frontend app lives in `react/`.
- Frontend tests live in `testing/frontend/`.
- Planned UI library: Material UI with MUI X Data Grid.
- Planned packages: `@mui/material`, `@emotion/react`, `@emotion/styled`, `@mui/icons-material`, and `@mui/x-data-grid`.

## App Structure

- App routing/state starts in `react/src/App.jsx`; page names include `dashboard`, `tickets`, `allocated`, `archived`, `add-bug`, `project-management`, `user-management`, and `audit-logs`.
- Ticket pages:
  - Active list: `react/src/pages/ViewTicketsPage.jsx`.
  - Allocated-to-me list: `react/src/pages/AllocatedPage.jsx`.
  - Closed/archive list: `react/src/pages/ArchivedPage.jsx`.
  - Create form: `react/src/pages/AddBugPage.jsx`.
- Shared ticket/report components:
  - Modal report viewer: `react/src/components/ReportPanel.jsx`.
  - Modal edit/close form: `react/src/components/BugReportFormPanel.jsx`.
  - Allocated options modal: `react/src/components/BugOptionsPanel.jsx`.
  - Sortable table: `react/src/components/TicketTable.jsx`.
  - Block text/image editor: `react/src/components/ReportBuilderEditor.jsx` and `react/src/report_builder.js`.
- API client modules live in `react/src/api/*`; do not inline `fetch` in pages.
- Ticket table helpers and sort accessors live in `react/src/table_utils.js`; add new sortable fields there.

## Product UI Direction

- Move the app from a centered custom dark card layout to a modern bug-operations dashboard.
- Support light and dark themes from the first MUI migration pass.
- Use a responsive shell with desktop navigation and mobile drawer behavior.
- Preserve all existing ticket workflows, auth behavior, role-gated navigation, API calls, and user-visible labels unless a test is deliberately updated.
- Prefer MUI components, theme tokens, `sx`, and component theme overrides over new global CSS.
- Keep custom CSS only for app-specific report-builder behavior, image layout, or visual effects that are not cleanly expressed through MUI.

## MUI Foundation

- Add a root MUI theme provider around the app.
- Use `ThemeProvider`, `createTheme`, `CssBaseline`, and MUI `colorSchemes` for light/dark mode.
- Add a theme toggle in the authenticated app shell.
- Put theme setup in a dedicated file such as `react/src/theme.js` or `react/src/theme/index.js`.
- Use theme component overrides for repeated styling instead of per-component one-off CSS.
- Define shared color mappings for status, severity, priority, and tags.

## App Shell Plan

- Replace the custom `NavBar` button strip and mobile CSS drawer with MUI `AppBar`, `Toolbar`, `Drawer`, `List`, `ListItemButton`, `ListItemIcon`, and `ListItemText`.
- Use a permanent or mini drawer on desktop and a temporary drawer on mobile.
- Keep current page-state navigation until a router is explicitly introduced.
- Keep role-gated links for projects, requests, and audit logs.
- Move notifications into an app bar `IconButton` with `Badge` and a MUI `Menu` or `Popover`.
- Keep logout visible in the shell, preferably in the drawer footer or account menu.

## Shared MUI Components

- Create shared status components for `SeverityChip`, `PriorityChip`, `StatusChip`, and `TagChip` using MUI `Chip`.
- Create shared `PageHeader`, `MetricCard`, `EmptyState`, `LoadingState`, and `ErrorAlert` components.
- Replace `.spinner` with MUI `CircularProgress`, `LinearProgress`, or `Skeleton` depending on context.
- Replace `.error-text` and `.success-text` with MUI `Alert` using stable accessible text.
- Replace custom context menus with MUI `Menu` while keeping right-click and long-press behavior where currently supported.

## Page Plans

### Login, Request Access, Setup Password

- Convert auth screens to MUI `Container`, `Card`, `CardContent`, `Typography`, `TextField`, `Select`, `MenuItem`, `Button`, `Alert`, and `CircularProgress`.
- Keep field labels and button names stable for tests.
- Give the unauthenticated experience the same visual brand as the dashboard.

### Dashboard

- Replace custom summary cards with MUI `Grid`, `Card`, `CardContent`, `Stack`, and `Typography`.
- Add stronger metric hierarchy for allocated tickets, visible projects, active tickets, urgent tickets, and unassigned tickets when data is available.
- Use MUI `Chip` for project and status metadata.
- Replace the dashboard ticket preview table with either a compact MUI X `DataGrid` or a card/list preview.

### View Tickets

- Use MUI X `DataGrid` for the active ticket list.
- Use a toolbar/filter card with `TextField`, `Select`, `MenuItem`, `ToggleButtonGroup`, `Button`, and export controls.
- Render status, severity, priority, and tags with shared MUI chip components.
- Move row actions into a dedicated actions column with buttons or an overflow `Menu`.
- Keep quick filters: All, Urgent, Recently Updated, Unassigned.
- Keep bulk assignment behavior and move the bulk assign flow into a MUI `Dialog`.

### Allocated Bugs

- Treat this page as a work queue.
- Use the same ticket grid and filter toolbar pattern as View Tickets.
- Use an empty-state component when no tickets are assigned.
- Convert `BugOptionsPanel` to a MUI `Dialog` with clear actions for Edit Bug Report, Modify Solution Steps, Edit Metadata, and Close Bug.
- Preserve existing API mappings for initial report edits, solution report edits, and close actions.

### Archived Tickets

- Use MUI X `DataGrid` with resolved date, resolved by, project, severity, priority, tags, and actions.
- Use MUI `Chip` for closed/archive status and metadata.
- Convert report viewing to a MUI `Dialog` with `Tabs` for Initial Bug Report and Solution/Fix Report.
- Convert reopen flow to a MUI `Dialog` with a multiline `TextField`.

### Add Bug

- Convert the long single-column form into structured MUI sections.
- Use `Card` or `Paper` sections for Issue Summary, Report Details, Metadata, and Evidence.
- Use `TextField`, `Select`, `MenuItem`, `RadioGroup`, `FormControl`, `FormLabel`, `FormControlLabel`, `Button`, and `Alert`.
- Consider `Accordion` or `Stepper` if the form remains visually long after the first MUI pass.
- Keep `ReportBuilderEditor` behavior intact; restyle its rows and controls with MUI after the outer form is migrated.

### Project Management

- Replace the sparse form with MUI `Card` sections for project selection, user allocation, and current allocation summary.
- Use `Autocomplete` or `Select` for project and user pickers.
- Use `List`, `ListItem`, `Avatar`, and `Chip` for allocated users when allocation data is shown.
- Consider MUI Transfer List only if multi-user allocation editing is added.

### Requests

- Use MUI `Tabs` for Human Requests and AI Agent Requests.
- Replace raw request tables with MUI X `DataGrid` or MUI `Table`.
- Replace custom request context menu with MUI `Menu`.
- Convert edit username overlay to MUI `Dialog`.
- Preserve setup-link and API-key behavior initially; a copy-to-clipboard dialog can be a later enhancement.

### Audit Logs

- Use a MUI filter card with `Grid`, `TextField`, `Select`, and `Button`.
- Use MUI X `DataGrid` for log rows because the page has high row count and long summaries.
- Use `Chip` for actor type and action family.
- Keep long summaries readable with wrapping or a details dialog.

## Report UI

- Current report data shape must remain unchanged:
  - `description` = initial submitted bug report text.
  - `reportImages` = initial submitted bug report images.
  - `postResolutionReport` / `resolutionNotes` = solution/fix report text.
  - `resolutionReportImages` = solution/fix report images.
  - `assignedAt` = when ticket became active; active time starts here, not at creation.
  - `resolvedByUserId` = user who closed/resolved; fallback to `assigneeUserId` only for old data.
- Replace custom report overlays with MUI `Dialog`, `DialogTitle`, `DialogContent`, and `DialogActions`.
- Use `Tabs` for initial and solution/fix report views.
- Use `Card`, `Paper`, `Stack`, `Typography`, and `Chip` for ticket summary and metadata.
- Use `TextField multiline` for comments and report editing.
- MUI `Dialog` should own focus trapping and Escape behavior; remove custom global Escape listeners after migration.
- Allocated page actions must remain unchanged functionally:
  - `Edit Bug Report` updates initial report through `updateInitialBugReport`.
  - `Modify Solution Steps` updates solution/fix report through `updateBugReport`.
  - `Close Bug` calls `closeBug` and archives the ticket.

## Data Grid Guidance

- Use MUI X Data Grid Community for ticket lists and audit logs unless a page only needs a small static table.
- Data Grid parents must have intrinsic dimensions, such as `Box sx={{ height: 520, width: '100%' }}`.
- Preserve current column labels, action labels, and sort defaults where practical.
- Be aware that Data Grid virtualizes rows; tests should assert visible behavior and accessible names rather than assuming every row is mounted.
- Keep `table_utils.js` as the source for date formatting, project-name fallback, and sort/accessor logic until Data Grid-specific replacements are intentionally introduced.

## CSS Cleanup Plan

- MUI should own forms, buttons, tables, dialogs, navigation, chips, loading states, alerts, and responsive shell behavior.
- `base.css` theme variables and global background should move into MUI theme and `CssBaseline`/global styles.
- `forms.css` global input/button/select/textarea styles should be removed as forms migrate to MUI controls.
- `nav.css` should be removed or reduced after the MUI app shell lands.
- `tables.css` should be reduced after ticket/admin tables move to MUI Data Grid or MUI Table.
- `panels.css` should be reduced after report/action panels move to MUI Dialog.
- `responsive.css` should shrink as MUI breakpoints, responsive `sx`, `Grid`, and Drawer variants take over.
- `projects.css` appears minimally used and should be removed or replaced during the Project Management migration.

## React Guidelines

- Functional components and hooks only; no class components.
- Keep state local unless cross-page sharing is required.
- Keep API calls in `react/src/api/*` modules, not inline in components or pages.
- Handle loading, success, empty, and error UI states explicitly.
- Validate form input on the client, but assume the server is source of truth.
- Preserve semantic labels, roles, dialog names, button names, and keyboard navigation behavior.
- Avoid adding memoization hooks by default unless surrounding code already uses them or there is a measured need.
- For expensive filter/search UI, consider `useDeferredValue` or `startTransition` only when needed.

## Accessibility And Testing Risks

- Keep accessible names stable because tests use Testing Library role, label, and text queries.
- MUI Dialog changes focus behavior; verify Escape close, click-away behavior when expected, and focus restoration.
- MUI Data Grid virtualization may require test updates for row assertions.
- Preserve right-click and long-press row action behavior, but add visible action affordances where possible.
- Do not rely on color alone for severity, priority, status, or alert meaning; chip labels must remain textual.
- Verify both desktop and mobile layouts with Playwright after shell, grid, and dialog migrations.

## Frontend Testing

- Test user-observable behavior, not implementation details.
- Use Testing Library queries by role/label/text first.
- Mock `fetch` per test and restore mocks in `afterEach`.
- Frontend tests for report flows are `testing/frontend/report-panel.test.jsx` and `testing/frontend/allocated-archived-reports.test.jsx`.
- Prefer targeted report-flow runs first: `cd react && npm run test -- ../testing/frontend/report-panel.test.jsx ../testing/frontend/allocated-archived-reports.test.jsx`.
- Run the full frontend suite with `cd react && npm run test` after frontend changes when practical.

## Frontend Commands

- Install dependencies: `cd react && npm install`.
- Run dev server: `cd react && npm run dev`.
- Build frontend: `cd react && npm run build`.
- Run one test file: `cd react && npm run test -- ../testing/frontend/login-page.test.jsx`.
- Run tests by name pattern: `cd react && npm run test -- -t "signs in and shows session card on success"`.
- No dedicated frontend lint script is currently configured in `react/package.json`.

## Recommended Implementation Order

1. Install MUI and add theme foundation with light/dark mode.
2. Migrate the app shell and notification center.
3. Add shared MUI primitives for page headers, chips, loading, empty states, and alerts.
4. Migrate report and action panels to MUI Dialog.
5. Migrate login and create/edit forms to MUI controls.
6. Add MUI X Data Grid and migrate ticket/admin tables.
7. Migrate admin pages and request management.
8. Remove obsolete CSS in waves after equivalent MUI components are live.
9. Run targeted tests, full frontend tests, build, and Playwright desktop/mobile checks.
