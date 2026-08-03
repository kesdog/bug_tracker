import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '../../react/node_modules/@testing-library/react/dist/index.js';
import ReportPanel from '../../react/src/components/ReportPanel';
import { ApiError } from '../../react/src/api/bugs';

afterEach(() => vi.restoreAllMocks());

describe('report panel', () => {
  it('renders report text and images in provided order', () => {
    const ticket = {
      id: 't-001',
      issueTitle: 'Export crash',
      assigneeUserId: 'usr_dev_001',
      status: 'closed',
      closeDate: '2026-01-01 12:00:00',
      postResolutionReport: 'Resolved with stream processing.',
      resolutionReportImages: [
        { name: 'first.png', contentType: 'image/png', dataUrl: 'data:image/png;base64,aGVsbG8=' },
        { name: 'second.jpg', contentType: 'image/jpeg', dataUrl: 'data:image/jpeg;base64,d29ybGQ=' }
      ]
    };

    render(<ReportPanel ticket={ticket} onClose={vi.fn()} />);

    expect(screen.getByText(/resolved with stream processing/i)).toBeInTheDocument();
    const images = screen.getAllByRole('img');
    expect(images).toHaveLength(2);
    expect(images[0]).toHaveAttribute('src', ticket.resolutionReportImages[0].dataUrl);
    expect(images[1]).toHaveAttribute('src', ticket.resolutionReportImages[1].dataUrl);
  });

  it('switches archived tickets between initial and solution reports', () => {
    const ticket = {
      id: 't-archived-001',
      issueTitle: 'Payment callback failed',
      projectName: 'Payments',
      reporterUserId: 'usr_qa_001',
      assigneeUserId: 'usr_dev_001',
      resolvedByUserId: 'usr_dev_001',
      status: 'closed',
      createdAt: '2026-01-01 09:00:00',
      assignedAt: '2026-01-01 10:15:00',
      closeDate: '2026-01-01 12:45:00',
      description: 'Initial callback report.',
      resolutionNotes: 'Patched webhook signature validation.'
    };

    render(<ReportPanel ticket={ticket} title="Archived Ticket Reports" showReportTabs onClose={vi.fn()} />);

    const ticketSummary = screen.getByLabelText(/ticket summary/i);
    expect(ticketSummary).toHaveTextContent('Payment callback failed');
    expect(screen.getByText('Payments')).toBeInTheDocument();
    expect(ticketSummary).toHaveTextContent('usr_qa_001');
    expect(ticketSummary).toHaveTextContent('usr_dev_001');
    expect(screen.getByText('0d 2h 30m')).toBeInTheDocument();
    expect(screen.getByText(/initial callback report/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('tab', { name: /solution/i }));

    expect(screen.getByText(/patched webhook signature validation/i)).toBeInTheDocument();
  });

  it('renders structured report fields and text evidence', () => {
    const ticket = {
      id: 't-structured-001',
      issueTitle: 'Checkout spinner',
      description: 'Checkout hangs after payment.',
      environment: 'Chrome 126 on Linux',
      expectedBehavior: 'Order confirmation appears.',
      actualBehavior: 'Spinner remains visible.',
      stepsToReproduce: '1. Add item\n2. Pay\n3. Watch spinner',
      frequency: 'frequent',
      textEvidence: [
        { name: 'console-log.txt', contentType: 'text/plain', text: 'POST /checkout 500' }
      ]
    };

    render(<ReportPanel ticket={ticket} onClose={vi.fn()} />);

    expect(screen.getByRole('heading', { level: 3, name: /ticket details/i })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: /ticket details/i })).toBeInTheDocument();
    expect(screen.getByText('Chrome 126 on Linux')).toBeInTheDocument();
    expect(screen.getByText('Order confirmation appears.')).toBeInTheDocument();
    expect(screen.getByText('Spinner remains visible.')).toBeInTheDocument();
    expect(screen.getByText(/console-log.txt/i)).toBeInTheDocument();
    expect(screen.getByText(/POST \/checkout 500/i)).toBeInTheDocument();
  });

  it('closes on X button click', () => {
    const onClose = vi.fn();
    render(<ReportPanel ticket={{ id: 't-002', issueTitle: 'No image' }} onClose={onClose} />);

    fireEvent.click(screen.getByRole('button', { name: /close report/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('groups agent attachments and downloads through the authenticated endpoint', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockResolvedValue({
      ok: true,
      headers: { get: () => 'attachment; filename="agent-proof.png"' },
      blob: async () => new Blob(['image-bytes'], { type: 'image/png' })
    });
    const createObjectURL = vi.fn(() => 'blob:agent-proof');
    const revokeObjectURL = vi.fn();
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: createObjectURL });
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: revokeObjectURL });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

    render(<ReportPanel token="secure-token" ticket={{
      id: 't-attachments',
      issueTitle: 'Agent evidence',
      description: 'Initial report',
      postResolutionReport: 'Solution report',
      attachments: [
        { id: 'att-initial', purpose: 'initial-report', name: 'initial.png', contentType: 'image/png', sizeBytes: 1024 },
        { id: 'att-close', purpose: 'close-report', name: 'agent-proof.png', contentType: 'image/png', sizeBytes: 2048 }
      ]
    }} onClose={vi.fn()} />);

    expect(screen.getByLabelText(/initial report attachments/i)).toHaveTextContent('initial.png');
    expect(screen.getByLabelText(/solution \/ close attachments/i)).toHaveTextContent('agent-proof.png');
    fireEvent.click(screen.getAllByRole('button', { name: /download/i })[1]);

    await waitFor(() => expect(fetchSpy).toHaveBeenCalledWith(
      expect.stringMatching(/\/api\/bugs\/t-attachments\/attachments\/att-close$/),
      expect.objectContaining({ headers: { Authorization: 'Bearer secure-token' } })
    ));
    expect(createObjectURL).toHaveBeenCalled();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:agent-proof');
  });

  it('uses persisted activity as the authoritative, ordered timeline', () => {
    render(<ReportPanel ticket={{
      id: 'timeline-1', issueTitle: 'State history', status: 'reopened', createdAt: '2026-01-01 08:00:00', closeDate: '2026-01-01 12:00:00',
      activity: [
        { id: 'b', kind: 'reopened', createdAt: '2026-01-01 10:00:00', actor: { userId: 'u2', username: 'sam', userType: 'human' }, fromStatus: 'closed', toStatus: 'reopened', version: 4 },
        { id: 'a', kind: 'created', createdAt: '2026-01-01 09:00:00', actor: { userId: 'u1', username: 'alex', userType: 'agent' }, changedFields: ['status'], version: 1 }
      ]
    }} onClose={vi.fn()} />);

    const timeline = screen.getByLabelText(/ticket activity timeline/i);
    expect(timeline).toHaveTextContent('Ticket created');
    expect(timeline).toHaveTextContent('Ticket reopened');
    expect(timeline).not.toHaveTextContent('Ticket resolved');
    expect(timeline.textContent.indexOf('Ticket created')).toBeLessThan(timeline.textContent.indexOf('Ticket reopened'));
    expect(timeline).toHaveTextContent('alex (u1) · agent');
    expect(timeline).toHaveTextContent('closed → reopened');
    expect(timeline).toHaveTextContent('Changed: status · Version 1');
    expect(screen.getByText(/returned to active work after closure/i)).toBeInTheDocument();
  });

  it('contacts a user in-ticket with a targeted comment and offers safe email', async () => {
    const onAddComment = vi.fn().mockResolvedValue({});
    render(<ReportPanel showReportTabs ticket={{
      id: 'bug-77', issueTitle: 'Card declined', status: 'open', reporterUserId: 'u1',
      reporter: { userId: 'u1', username: 'alex', userType: 'human', email: 'alex@example.com' }
    }} onAddComment={onAddComment} onClose={vi.fn()} />);

    const contactButton = screen.getByRole('button', { name: /contact alex/i });
    expect(contactButton).not.toHaveTextContent(/contact/i);
    fireEvent.click(contactButton);
    const email = screen.getByRole('menuitem', { name: /email alex/i });
    expect(email).toHaveAttribute('href', expect.stringContaining('mailto:alex%40example.com'));
    expect(decodeURIComponent(email.getAttribute('href'))).toContain('[Bug Tracker] bug-77: Card declined');
    expect(decodeURIComponent(email.getAttribute('href'))).not.toContain('report');
    fireEvent.click(screen.getByRole('menuitem', { name: /contact in ticket/i }));
    expect(screen.getByLabelText(/add comment/i)).toHaveValue('@alex ');
    fireEvent.click(screen.getByRole('button', { name: /add comment/i }));
    await waitFor(() => expect(onAddComment).toHaveBeenCalledWith('bug-77', '@alex', 'u1'));
  });

  it('shows permission remediation and submits an access request', async () => {
    const fetchSpy = vi.spyOn(global, 'fetch').mockResolvedValue({ ok: true, status: 204 });
    const error = new ApiError('Membership is required.', { status: 403, errorCode: 'ticket_access_denied', payload: {
      errorCode: 'ticket_access_denied', message: 'Membership is required.', steps: ['Ask the project owner.'], contacts: [{ username: 'owner', role: 'senior' }], requestAccessPath: '/api/projects/p1/access-requests'
    } });
    render(<ReportPanel error={error} token="token" onClose={vi.fn()} />);
    expect(screen.getByLabelText(/ticket access help/i)).toHaveTextContent('owner (senior)');
    fireEvent.click(screen.getByRole('button', { name: /request project access/i }));
    await waitFor(() => expect(fetchSpy).toHaveBeenCalledWith(expect.stringContaining('/api/projects/p1/access-requests'), expect.objectContaining({ method: 'POST', body: JSON.stringify({ reason: 'Please grant access so I can review and help resolve this ticket.' }) })));
    expect(await screen.findByText(/access request submitted/i)).toBeInTheDocument();
  });
});
