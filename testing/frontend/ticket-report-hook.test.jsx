import React from '../../react/node_modules/react/index.js';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '../../react/node_modules/@testing-library/react/dist/index.js';
import useTicketReport from '../../react/src/hooks/useTicketReport';

afterEach(() => vi.restoreAllMocks());

function deferred() {
  let resolve;
  const promise = new Promise((next) => { resolve = next; });
  return { promise, resolve };
}

function HookHarness() {
  const report = useTicketReport('token');
  return (
    <div>
      <button onClick={() => report.openReport({ id: 'first' })}>First</button>
      <button onClick={() => report.openReport({ id: 'second' })}>Second</button>
      <button onClick={report.closeReport}>Close</button>
      <span>{report.ticket?.issueTitle || (report.loading ? 'Loading' : report.error)}</span>
    </div>
  );
}

describe('shared ticket report hook', () => {
  it('always fetches detail and ignores a stale earlier response', async () => {
    const first = deferred();
    const second = deferred();
    const fetchSpy = vi.spyOn(global, 'fetch')
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise);
    render(<HookHarness />);

    fireEvent.click(screen.getByRole('button', { name: 'First' }));
    fireEvent.click(screen.getByRole('button', { name: 'Second' }));
    second.resolve({ ok: true, json: async () => ({ id: 'second', issueTitle: 'Latest report' }) });
    expect(await screen.findByText('Latest report')).toBeInTheDocument();
    first.resolve({ ok: true, json: async () => ({ id: 'first', issueTitle: 'Stale report' }) });

    expect(await screen.findByText('Latest report')).toBeInTheDocument();
    expect(screen.queryByText('Stale report')).not.toBeInTheDocument();
    expect(fetchSpy).toHaveBeenCalledTimes(2);
  });

  it('invalidates an in-flight detail request when the report closes', async () => {
    const request = deferred();
    vi.spyOn(global, 'fetch').mockImplementationOnce(() => request.promise);
    render(<HookHarness />);

    fireEvent.click(screen.getByRole('button', { name: 'First' }));
    expect(screen.getByText('Loading')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    request.resolve({ ok: true, json: async () => ({ id: 'first', issueTitle: 'Too late' }) });

    await Promise.resolve();
    expect(screen.queryByText('Too late')).not.toBeInTheDocument();
    expect(screen.queryByText('Loading')).not.toBeInTheDocument();
  });
});
