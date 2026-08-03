import React, { useEffect, useState } from 'react';
import { fetchActiveBugs, fetchBugById, fetchClosedBugs, updateInitialBugReport } from '../api/bugs';
import BugReportFormPanel from '../components/BugReportFormPanel';
import { PriorityChip, SeverityChip } from '../components/MuiPrimitives';
import ReportPanel from '../components/ReportPanel';
import TicketTable from '../components/TicketTable';
import useTicketReport from '../hooks/useTicketReport';
import { clearReviewedConflictFields, recoverTicketConflict } from '../concurrency';
import { useI18n } from '../i18n';

function getInitialReportText(ticket) {
  return ticket?.description || '';
}

export default function SubmittedReportsPage({ token, currentUserId }) {
  const { t } = useI18n();
  const [tickets, setTickets] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [reloadKey, setReloadKey] = useState(0);
  const report = useTicketReport(token);
  const [editTicket, setEditTicket] = useState(null);
  const [editSubmitting, setEditSubmitting] = useState(false);
  const [editError, setEditError] = useState('');
  const [editConflict, setEditConflict] = useState(null);

  useEffect(() => {
    let isActive = true;

    async function loadSubmitted() {
      setLoading(true);
      setError('');
      try {
        const filters = { reporterUserId: currentUserId };
        const [active, closed] = await Promise.all([
          fetchActiveBugs(token, 100, '', filters),
          fetchClosedBugs(token, 100, '', filters)
        ]);

        if (isActive) {
          setTickets([...(Array.isArray(active) ? active : []), ...(Array.isArray(closed) ? closed : [])]);
        }
      } catch (err) {
        if (isActive) {
          setTickets([]);
           setError(err.message || t('pages.submittedReports.errors.loadTickets', 'Unable to load submitted tickets.'));
        }
      } finally {
        if (isActive) {
          setLoading(false);
        }
      }
    }

    loadSubmitted();

    return () => {
      isActive = false;
    };
  }, [currentUserId, reloadKey, token]);

  async function openEdit(ticket) {
    setEditError('');
    setEditConflict(null);
    try {
      setEditTicket(await fetchBugById(token, ticket.id));
    } catch (err) {
       setError(err.message || t('pages.submittedReports.errors.loadTicketDetails', 'Unable to load ticket details.'));
    }
  }

  async function submitEdit({ text, images }) {
    if (!editTicket) {
      return;
    }

    setEditSubmitting(true);
    setEditError('');
    try {
      await updateInitialBugReport(token, editTicket.id, text, images, editConflict?.latestTicket?.version || editTicket.version);
      setEditTicket(null);
      setReloadKey((value) => value + 1);
    } catch (err) {
      const recovered = await recoverTicketConflict(err, token);
      if (recovered) {
        setEditConflict(recovered);
      } else {
         setEditError(err.message || t('pages.submittedReports.errors.editReport', 'Unable to edit submitted report.'));
      }
    } finally {
      setEditSubmitting(false);
    }
  }

  const columns = [
    { key: 'issueTitle', label: t('common.title', 'Title'), sortable: true, defaultDirection: 'asc' },
    { key: 'status', label: t('common.status', 'Status'), sortable: true, defaultDirection: 'asc' },
    { key: 'assigneeUserId', label: t('common.assignee', 'Assignee'), sortable: true, defaultDirection: 'asc' },
    { key: 'severity', label: t('common.severity', 'Severity'), sortable: true, defaultDirection: 'desc', render: (ticket) => <SeverityChip value={ticket.severity} /> },
    { key: 'priority', label: t('common.priority', 'Priority'), sortable: true, defaultDirection: 'desc', render: (ticket) => <PriorityChip value={ticket.priority} /> }
  ];

  const rowMenuItems = [
    { key: 'view-details', label: t('common.viewReports', 'View Reports'), onSelect: report.openReport },
    { key: 'edit-report', label: t('common.edit', 'Edit'), shouldShow: (ticket) => ticket.status !== 'closed', onSelect: openEdit }
  ];

  return (
    <section className="dashboard">
       <h2>{t('pages.submittedReports.title', 'Submitted Reports')}</h2>
       <p className="subtitle">{t('pages.submittedReports.subtitle', 'Track every bug report you submitted and open details or edits from the row menu.')}</p>

      {error ? <p role="alert" className="error-text">{error}</p> : null}
       {loading ? <div className="spinner" aria-label={t('pages.submittedReports.loading', 'loading submitted reports')} /> : null}
       {!loading && !error && tickets.length === 0 ? <p className="dashboard-empty">{t('pages.submittedReports.empty', 'No submitted reports yet.')}</p> : null}

      {!loading && tickets.length > 0 ? (
        <TicketTable tickets={tickets} columns={columns} defaultSort={{ key: 'status', direction: 'asc' }} rowMenuItems={rowMenuItems} />
      ) : null}

      {report.isOpen ? (
        <ReportPanel
          ticket={report.ticket}
          loading={report.loading}
          error={report.error}
          token={token}
          showReportTabs={['closed', 'cancelled'].includes(report.ticket?.status)}
          onAddComment={report.addComment}
          onClose={report.closeReport}
        />
      ) : null}

      {editTicket ? (
        <BugReportFormPanel
          ticket={editTicket}
           title={t('pages.submittedReports.editBugReport', 'Edit Bug Report')}
           submitLabel={t('pages.submittedReports.saveBugReport', 'Save Bug Report')}
           notesLabel={t('pages.submittedReports.bugReport', 'Bug Report')}
          initialText={getInitialReportText(editTicket)}
          initialImages={editTicket.reportImages || []}
          submitting={editSubmitting}
          error={editError}
          conflict={editConflict?.conflict}
          latestTicket={editConflict?.latestTicket}
          conflictFields={['description', 'reportImages']}
          conflictRefreshError={editConflict?.refreshError}
          onConflictReview={(fields) => setEditConflict((current) => current ? { ...current, conflict: clearReviewedConflictFields(current.conflict, fields) } : current)}
          onSubmit={submitEdit}
          onClose={() => {
            setEditTicket(null);
            setEditSubmitting(false);
            setEditError('');
            setEditConflict(null);
          }}
        />
      ) : null}
    </section>
  );
}
