import React from '../../react/node_modules/react/index.js';
import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, within } from '../../react/node_modules/@testing-library/react/dist/index.js';
import TicketTable from '../../react/src/components/TicketTable';

const tickets = [
  { id: 'ticket-1', issueTitle: 'Zulu regression', status: 'open', tags: ['front-end', 'pricing'] },
  { id: 'ticket-2', issueTitle: 'Alpha regression', status: 'todo', tags: ['operations'] }
];

describe('ticket table formatting', () => {
  it('keeps tags and report actions out of the default table while preserving sortable headers', () => {
    render(
      <TicketTable
        tickets={tickets}
        columns={[
          { key: 'issueTitle', label: 'Bug', sortable: true },
          { key: 'tags', label: 'Tags', sortable: true },
          { key: 'viewReports', label: 'Reports', sortable: false, render: () => <button type="button">View Reports</button> }
        ]}
      />
    );

    const grid = screen.getByRole('grid', { name: 'Ticket table' });
    expect(within(grid).queryByRole('columnheader', { name: /tags/i })).not.toBeInTheDocument();
    expect(within(grid).queryByRole('columnheader', { name: /reports/i })).not.toBeInTheDocument();
    expect(within(grid).queryByText('front-end')).not.toBeInTheDocument();
    expect(within(grid).queryByRole('button', { name: /view reports/i })).not.toBeInTheDocument();

    const bugHeader = within(grid).getByRole('columnheader', { name: /bug/i });
    fireEvent.click(bugHeader);
    expect(bugHeader).toHaveAttribute('aria-sort', 'ascending');
    fireEvent.click(bugHeader);
    expect(bugHeader).toHaveAttribute('aria-sort', 'descending');
  });
});
