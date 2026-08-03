import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '../../react/node_modules/@testing-library/react/dist/index.js';
import userEvent from '../../react/node_modules/@testing-library/user-event/dist/esm/index.js';
import { ApiError, clearBugCache, updateBugMetadata } from '../../react/src/api/bugs';
import SubmittedReportsPage from '../../react/src/pages/SubmittedReportsPage';
import TicketMetadataPanel from '../../react/src/components/TicketMetadataPanel';
import BugReportFormPanel from '../../react/src/components/BugReportFormPanel';
import ReopenTicketPanel from '../../react/src/components/ReopenTicketPanel';
import { ConflictFieldResolution } from '../../react/src/components/ConcurrencyConflict';
import { clearReviewedConflictFields } from '../../react/src/concurrency';

afterEach(() => {
  vi.restoreAllMocks();
  clearBugCache();
});

function conflictPayload(overrides = {}) {
  return {
    error: 'Ticket was updated by another user.',
    errorCode: 'ticket_version_conflict',
    ticketId: 'submitted-001',
    expectedVersion: 3,
    currentVersion: 4,
    currentStatus: 'open',
    changedFields: ['description'],
    recovery: 'Fetch the latest ticket and review your changes.',
    ...overrides
  };
}

function MetadataConflictHarness() {
  const [conflict, setConflict] = React.useState(conflictPayload({ changedFields: ['priority'] }));
  return (
    <TicketMetadataPanel
      ticket={{ id: 'submitted-001', issueTitle: 'Search loses filters', bugType: 'api', severity: 'high', priority: 'p2', tags: [], version: 4 }}
      latestTicket={{ id: 'submitted-001', issueTitle: 'Search loses filters', bugType: 'api', severity: 'high', priority: 'p1', tags: [], version: 5 }}
      conflict={conflict}
      onConflictReview={(fields) => setConflict((current) => clearReviewedConflictFields(current, fields))}
      onSubmit={() => {}}
      onClose={() => {}}
    />
  );
}

function CloseConflictHarness({ latestStatus = 'reopened' }) {
  const [conflict, setConflict] = React.useState(conflictPayload({ changedFields: ['status'], currentStatus: latestStatus }));
  return (
    <BugReportFormPanel
      ticket={{ id: 'close-001', issueTitle: 'Checkout crash', status: 'open', version: 2 }}
      latestTicket={{ id: 'close-001', issueTitle: 'Checkout crash', status: latestStatus, version: 3 }}
      title="Close Bug"
      submitLabel="Close Bug"
      initialText="My resolution draft"
      initialImages={[]}
      conflict={conflict}
      actionKind="close"
      onConflictReview={(fields) => setConflict((current) => clearReviewedConflictFields(current, fields))}
      onSubmit={() => {}}
      onClose={() => {}}
    />
  );
}

function ReopenConflictHarness() {
  const [conflict, setConflict] = React.useState(conflictPayload({ changedFields: ['priority'], currentStatus: 'closed' }));
  return (
    <ReopenTicketPanel
      ticket={{ id: 'reopen-001', issueTitle: 'Webhook failed', status: 'closed', priority: 'p2', version: 4 }}
      latestTicket={{ id: 'reopen-001', issueTitle: 'Webhook failed', status: 'closed', priority: 'p1', version: 5 }}
      conflict={conflict}
      onConflictReview={(fields) => setConflict((current) => clearReviewedConflictFields(current, fields))}
      onSubmit={() => {}}
      onClose={() => {}}
    />
  );
}

describe('optimistic concurrency handling', () => {
  it('preserves structured conflict data and marks timed out writes as uncertain', async () => {
    const payload = conflictPayload();
    vi.spyOn(global, 'fetch').mockResolvedValueOnce({ ok: false, status: 409, json: async () => payload });

    await expect(updateBugMetadata('token', 'submitted-001', { severity: 'high' }, 3)).rejects.toMatchObject({
      name: 'ApiError',
      status: 409,
      errorCode: 'ticket_version_conflict',
      payload,
      uncertain: false
    });

    const timeout = new Error('aborted');
    timeout.name = 'AbortError';
    global.fetch.mockRejectedValueOnce(timeout);
    await expect(updateBugMetadata('token', 'submitted-001', { severity: 'urgent' }, 4)).rejects.toEqual(expect.objectContaining({
      name: 'ApiError',
      errorCode: 'request_timeout',
      uncertain: true
    }));
    expect(ApiError.prototype).toBeInstanceOf(Error);
  });

  it('preserves the report draft and requires explicit conflict resolution before retrying against the latest version', async () => {
    let detailLoads = 0;
    let patchCalls = 0;
    const fetchSpy = vi.spyOn(global, 'fetch').mockImplementation(async (url, options = {}) => {
      const target = String(url);
      const method = options.method || 'GET';

      if (target.includes('/api/bugs?status=active') && method === 'GET') {
        return { ok: true, json: async () => [{ id: 'submitted-001', issueTitle: 'Search loses filters', status: 'open', reporterUserId: 'usr-me', version: 3 }] };
      }
      if (target.includes('/api/bugs?status=closed') && method === 'GET') {
        return { ok: true, json: async () => [] };
      }
      if (target.endsWith('/api/bugs/submitted-001') && method === 'GET') {
        detailLoads += 1;
        return {
          ok: true,
          json: async () => detailLoads === 1
            ? { id: 'submitted-001', issueTitle: 'Search loses filters', status: 'open', description: 'Original report', reportImages: [], version: 3 }
            : { id: 'submitted-001', issueTitle: 'Search loses filters', status: 'open', description: 'Server report', reportImages: [], version: 4 }
        };
      }
      if (target.endsWith('/api/bugs/submitted-001/initial-report') && method === 'PATCH') {
        patchCalls += 1;
        return patchCalls === 1
          ? { ok: false, status: 409, json: async () => conflictPayload() }
          : { ok: true, json: async () => ({ id: 'submitted-001' }) };
      }

      throw new Error(`Unhandled fetch: ${method} ${target}`);
    });

    render(<SubmittedReportsPage token="token" currentUserId="usr-me" />);
    const user = userEvent.setup();

    fireEvent.click(await screen.findByText('Search loses filters'));
    await user.click(screen.getByRole('menuitem', { name: 'Edit' }));
    const editor = await screen.findByDisplayValue('Original report');
    await user.clear(editor);
    await user.type(editor, 'My carefully written draft');
    await user.click(screen.getByRole('button', { name: 'Save Bug Report' }));

    expect(await screen.findByText(/newer ticket changes found/i)).toBeInTheDocument();
    expect(screen.getByDisplayValue('My carefully written draft')).toBeInTheDocument();
    expect(screen.getByText('Server report')).toBeInTheDocument();
    expect(screen.getAllByText('My carefully written draft')).toHaveLength(2);
    expect(screen.getByText(/changed on the server\. review this value/i)).toBeInTheDocument();
    expect(document.querySelector('.report-builder-editor.conflict-field')).not.toBeNull();
    expect(detailLoads).toBe(2);
    const saveButton = screen.getByRole('button', { name: 'Save Bug Report' });
    expect(saveButton).toBeDisabled();
    const reportEditor = screen.getByRole('textbox', { name: /bug report text block 1/i });
    expect(reportEditor).toHaveAttribute('aria-describedby', 'report-text-conflict-submitted-001');
    expect(document.getElementById(reportEditor.getAttribute('aria-describedby'))).toBeInTheDocument();

    const patchCall = fetchSpy.mock.calls.find(([requestUrl, requestOptions]) => String(requestUrl).endsWith('/initial-report') && requestOptions?.method === 'PATCH');
    expect(JSON.parse(patchCall[1].body)).toEqual({ reportText: 'My carefully written draft', reportImages: [], expectedVersion: 3 });

    fireEvent.focus(reportEditor);
    expect(screen.getByText(/changed on the server\. review this value/i)).toBeInTheDocument();
    expect(saveButton).toBeDisabled();

    await user.click(screen.getByRole('button', { name: /keep my draft for bug report/i }));
    expect(screen.getByDisplayValue('My carefully written draft')).toBeInTheDocument();
    expect(document.querySelector('.report-builder-editor.conflict-field')).toBeNull();
    expect(saveButton).toBeEnabled();
    await user.click(saveButton);

    await waitFor(() => expect(patchCalls).toBe(2));
    const retryBody = JSON.parse(fetchSpy.mock.calls.filter(([requestUrl, requestOptions]) => String(requestUrl).endsWith('/initial-report') && requestOptions?.method === 'PATCH')[1][1].body);
    expect(retryBody).toEqual({ reportText: 'My carefully written draft', reportImages: [], expectedVersion: 4 });
  });

  it('treats metadata conflicts as warnings and keeps them unresolved on focus', async () => {
    render(<MetadataConflictHarness />);

    const priority = screen.getByRole('combobox', { name: /^priority$/i });
    const severity = screen.getByRole('combobox', { name: /^severity$/i });
    expect(priority).toHaveClass('conflict-field');
    expect(severity).not.toHaveClass('conflict-field');
    expect(priority).toHaveAttribute('aria-describedby', 'metadata-priority-conflict');
    expect(priority).not.toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByText('p1')).toBeInTheDocument();
    expect(screen.getByText(/changed on the server\. review this value/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save metadata/i })).toBeDisabled();

    fireEvent.focus(priority);
    expect(priority).toHaveClass('conflict-field');
    expect(screen.getByRole('button', { name: /save metadata/i })).toBeDisabled();

    fireEvent.click(screen.getByRole('button', { name: /use latest value for priority/i }));
    expect(priority).toHaveValue('p1');
    expect(priority).not.toHaveClass('conflict-field');
    expect(screen.getByRole('button', { name: /save metadata/i })).toBeEnabled();
  });

  it('requires explicit reconfirmation for still-valid close and reopen actions', async () => {
    const user = userEvent.setup();
    const { unmount } = render(<CloseConflictHarness />);
    const closeButton = screen.getByRole('button', { name: /^close bug$/i });

    expect(screen.getByText('reopened')).toBeInTheDocument();
    expect(screen.getByDisplayValue('My resolution draft')).toBeInTheDocument();
    expect(closeButton).toBeDisabled();
    await user.click(screen.getByRole('button', { name: /mark reviewed/i }));
    expect(closeButton).toBeDisabled();
    await user.click(screen.getByRole('button', { name: /reconfirm close/i }));
    expect(closeButton).toBeEnabled();

    unmount();
    render(<ReopenConflictHarness />);
    const reopenButton = screen.getByRole('button', { name: /^reopen ticket$/i });
    await user.type(screen.getByLabelText(/reason/i), 'The production failure returned.');
    expect(screen.getByText('p1')).toBeInTheDocument();
    expect(reopenButton).toBeDisabled();
    await user.click(screen.getByRole('button', { name: /mark reviewed/i }));
    await user.click(screen.getByRole('button', { name: /reconfirm reopen/i }));
    expect(reopenButton).toBeEnabled();
  });

  it('explains when a close action became obsolete', () => {
    render(<CloseConflictHarness latestStatus="closed" />);
    expect(screen.getByText(/closing it is obsolete/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /reconfirm close/i })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^close bug$/i })).toBeDisabled();
  });

  it('associates image-only conflicts with image controls and the editor group, not textareas', () => {
    render(
      <BugReportFormPanel
        ticket={{ id: 'images-001', issueTitle: 'Screenshot stale', description: 'Steps', reportImages: [], version: 2 }}
        latestTicket={{ id: 'images-001', issueTitle: 'Screenshot stale', description: 'Steps', reportImages: [{ name: 'server.png', dataUrl: 'data:image/png;base64,AA==' }], version: 3 }}
        title="Edit Bug Report"
        submitLabel="Save Bug Report"
        notesLabel="Bug Report"
        initialText="My text draft"
        initialImages={[]}
        conflict={conflictPayload({ ticketId: 'images-001', changedFields: ['reportImages'] })}
        conflictFields={['description', 'reportImages']}
        onConflictReview={() => {}}
        onSubmit={() => {}}
        onClose={() => {}}
      />
    );

    const textarea = screen.getByRole('textbox', { name: /bug report text block 1/i });
    const editorGroup = screen.getByRole('group', { name: /bug report editor/i });
    const addImage = screen.getByRole('button', { name: /^add image below$/i });
    expect(textarea).not.toHaveAttribute('aria-describedby');
    expect(editorGroup).toHaveAttribute('aria-describedby', 'report-images-conflict-images-001');
    expect(addImage).toHaveAttribute('aria-describedby', 'report-images-conflict-images-001');
    expect(document.getElementById('report-images-conflict-images-001')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: /bug report images/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /keep my draft for bug report images/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /use latest value for bug report images/i })).toBeInTheDocument();
  });

  it('labels repeated conflict sections and actions with their fields', () => {
    render(
      <>
        <ConflictFieldResolution field="priority" localValue="p2" latestValue="p1" descriptionId="priority-choice" onKeep={() => {}} onUseLatest={() => {}} />
        <ConflictFieldResolution field="severity" localValue="mid" latestValue="high" descriptionId="severity-choice" onKeep={() => {}} onUseLatest={() => {}} />
      </>
    );

    expect(screen.getByRole('region', { name: /^priority$/i })).toHaveAttribute('aria-labelledby', 'priority-choice-title');
    expect(screen.getByRole('region', { name: /^severity$/i })).toHaveAttribute('aria-labelledby', 'severity-choice-title');
    expect(screen.getByRole('button', { name: /keep my draft for priority/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /keep my draft for severity/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /use latest value for priority/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /use latest value for severity/i })).toBeInTheDocument();
  });
});
