import React from 'react';
import AllocatePanel from './AllocatePanel';
import BugReportFormPanel from './BugReportFormPanel';
import BulkAllocatePanel from './BulkAllocatePanel';
import ReportPanel from './ReportPanel';
import TicketMetadataPanel from './TicketMetadataPanel';
import { clearReviewedConflictFields } from '../concurrency';
import { ACTION_EDIT_INITIAL, getEditableReportText, getInitialReportText, hasSolutionReport } from '../view_tickets_config';

export default function ViewTicketPanels(props) {
  const { report, token, canModifyFromView, openModifyFromReport, actionTicket, actionType, actionSubmitting, actionError, actionConflict, setActionConflict, submitModifyForm, closeActionPanel, closeTicket, closeSubmitting, closeError, closeConflict, setCloseConflict, submitCloseForm, closeClosePanel, allocateTicket, assignees, selectedAssignee, allocateLoading, allocateSubmitting, allocateError, allocateConflict, setAllocateConflict, setSelectedAssignee, handleAllocate, closeAllocatePanel, metadataTicket, metadataSubmitting, metadataError, metadataConflict, setMetadataConflict, submitMetadataForm, closeMetadataPanel, bulkPanelOpen, quickFilteredTickets, bulkTargetIds, bulkAssignee, bulkLoading, bulkSubmitting, bulkError, bulkConflicts, setBulkConflicts, bulkLatestVersions, setBulkAssignee, submitBulkAssign, closeBulkPanel } = props;
  return (
    <>
      {report.isOpen ? <ReportPanel ticket={report.ticket} loading={report.loading} error={report.error} token={token} actionLabel={canModifyFromView ? 'Modify Solution Steps' : ''} onAction={canModifyFromView ? openModifyFromReport : null} onAddComment={report.addComment} onClose={report.closeReport} /> : null}
      {actionTicket ? (
        <BugReportFormPanel
          ticket={actionTicket}
          title={actionType === ACTION_EDIT_INITIAL ? 'Edit Bug Report' : hasSolutionReport(actionTicket) ? 'Modify Solution Steps' : 'Create Solution'}
          submitLabel="Save Report"
          notesLabel={actionType === ACTION_EDIT_INITIAL ? 'Bug Report' : 'Solution Steps'}
          initialText={actionType === ACTION_EDIT_INITIAL ? getInitialReportText(actionTicket) : getEditableReportText(actionTicket)}
          initialImages={actionType === ACTION_EDIT_INITIAL ? actionTicket.reportImages || [] : actionTicket.resolutionReportImages || []}
          submitting={actionSubmitting} error={actionError} conflict={actionConflict?.conflict} latestTicket={actionConflict?.latestTicket}
          conflictFields={actionType === ACTION_EDIT_INITIAL ? ['description', 'reportImages'] : ['postResolutionReport', 'resolutionNotes', 'resolutionReportImages']}
          conflictRefreshError={actionConflict?.refreshError}
          onConflictReview={(fields) => setActionConflict((current) => current ? { ...current, conflict: clearReviewedConflictFields(current.conflict, fields) } : current)}
          onSubmit={submitModifyForm} onClose={closeActionPanel}
        />
      ) : null}
      {closeTicket ? (
        <BugReportFormPanel ticket={closeTicket} title="Close Bug" submitLabel="Close Bug" notesLabel="Solution Steps" initialText={getEditableReportText(closeTicket)} initialImages={closeTicket?.resolutionReportImages || []} submitting={closeSubmitting} error={closeError} conflict={closeConflict?.conflict} latestTicket={closeConflict?.latestTicket} conflictRefreshError={closeConflict?.refreshError} actionKind="close" onConflictReview={(fields) => setCloseConflict((current) => current ? { ...current, conflict: clearReviewedConflictFields(current.conflict, fields) } : current)} onSubmit={submitCloseForm} onClose={closeClosePanel} />
      ) : null}
      {allocateTicket ? (
        <AllocatePanel ticket={allocateTicket} users={assignees} selectedAssignee={selectedAssignee} loading={allocateLoading} submitting={allocateSubmitting} error={allocateError} conflict={allocateConflict?.conflict} latestTicket={allocateConflict?.latestTicket} conflictRefreshError={allocateConflict?.refreshError} onConflictReview={(fields) => setAllocateConflict((current) => current ? { ...current, conflict: clearReviewedConflictFields(current.conflict, fields) } : current)} onAssigneeChange={setSelectedAssignee} onAllocate={handleAllocate} onClose={closeAllocatePanel} />
      ) : null}
      {metadataTicket ? (
        <TicketMetadataPanel ticket={metadataTicket} submitting={metadataSubmitting} error={metadataError} conflict={metadataConflict?.conflict} latestTicket={metadataConflict?.latestTicket} conflictRefreshError={metadataConflict?.refreshError} onConflictReview={(fields) => setMetadataConflict((current) => current ? { ...current, conflict: clearReviewedConflictFields(current.conflict, fields) } : current)} onSubmit={submitMetadataForm} onClose={closeMetadataPanel} />
      ) : null}
      {bulkPanelOpen ? (
        <BulkAllocatePanel tickets={quickFilteredTickets} ticketIds={bulkTargetIds} users={assignees} selectedAssignee={bulkAssignee} loading={bulkLoading} submitting={bulkSubmitting} error={bulkError} conflicts={bulkConflicts} retrying={bulkConflicts.length > 0 || Boolean(bulkError)} retryReady={bulkTargetIds.length > 0 && bulkTargetIds.every((ticketId) => Number.isInteger(bulkLatestVersions[ticketId]))} onConflictReview={(fields, ticketId) => setBulkConflicts((current) => current.map((conflict) => conflict.ticketId === ticketId ? clearReviewedConflictFields(conflict, fields) : conflict))} onAssigneeChange={setBulkAssignee} onAllocate={submitBulkAssign} onClose={closeBulkPanel} />
      ) : null}
    </>
  );
}
