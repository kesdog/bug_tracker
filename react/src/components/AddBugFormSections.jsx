import React from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormHelperText from '@mui/material/FormHelperText';
import FormLabel from '@mui/material/FormLabel';
import Radio from '@mui/material/Radio';
import RadioGroup from '@mui/material/RadioGroup';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { ADD_PROJECT_OPTION, BUG_TYPES, CORE_TAGS, FREQUENCIES, MAX_TEXT_EVIDENCE_FILES, PRIORITIES, SEVERITIES } from '../add_bug_form';
import { formatUserIdentity } from '../user_identity';
import ReportBuilderEditor from './ReportBuilderEditor';
import { useI18n } from '../i18n';

function NativeOptions({ options }) {
  return options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>);
}

export function BugReportFields({ form, errors, reportBuilder, reportBuilderError, submitting, onFieldChange, onBuilderChange, onBuilderError }) {
  const { t } = useI18n();
  return (
    <>
      <TextField id="issueTitle" name="issueTitle" label={t('addBug.issueTitle', 'Issue title')} value={form.issueTitle} onChange={(event) => onFieldChange('issueTitle', event.target.value)} placeholder={t('addBug.issueTitlePlaceholder', 'Describe the bug briefly')} error={Boolean(errors.issueTitle)} helperText={errors.issueTitle || t('addBug.issueTitleHelp', 'Give the issue a short, searchable title.')} fullWidth slotProps={{ htmlInput: { maxLength: 200 } }} />
      <Typography component="h3" variant="h6">{t('addBug.description', 'Bug Description')}</Typography>
      <ReportBuilderEditor builder={reportBuilder} submitting={submitting} error={reportBuilderError} onChange={onBuilderChange} onError={onBuilderError} />
      {errors.description ? <p role="alert" className="error-text">{errors.description}</p> : null}
      <div className="structured-report-grid">
        <TextField id="environment" label={t('addBug.environment', 'Environment')} value={form.environment} onChange={(event) => onFieldChange('environment', event.target.value)} placeholder={t('addBug.environmentPlaceholder', 'Browser, OS, device, API version')} helperText={t('addBug.environmentHelp', 'Optional: browser, OS, device, API version.')} fullWidth slotProps={{ htmlInput: { maxLength: 500 } }} />
        <TextField id="expectedBehavior" label={t('addBug.expectedBehavior', 'Expected behavior')} multiline minRows={3} value={form.expectedBehavior} onChange={(event) => onFieldChange('expectedBehavior', event.target.value)} placeholder={t('addBug.expectedBehaviorPlaceholder', 'What should have happened?')} fullWidth slotProps={{ htmlInput: { maxLength: 2000 } }} />
        <TextField id="actualBehavior" label={t('addBug.actualBehavior', 'Actual behavior')} multiline minRows={3} value={form.actualBehavior} onChange={(event) => onFieldChange('actualBehavior', event.target.value)} placeholder={t('addBug.actualBehaviorPlaceholder', 'What happened instead?')} fullWidth slotProps={{ htmlInput: { maxLength: 2000 } }} />
        <TextField id="stepsToReproduce" label={t('addBug.stepsToReproduce', 'Steps to reproduce')} multiline minRows={4} value={form.stepsToReproduce} onChange={(event) => onFieldChange('stepsToReproduce', event.target.value)} placeholder={'1. Open...\n2. Click...\n3. Observe...'} fullWidth slotProps={{ htmlInput: { maxLength: 4000 } }} />
        <TextField id="frequency" label="Frequency" value={form.frequency} onChange={(event) => onFieldChange('frequency', event.target.value)} select fullWidth slotProps={{ select: { native: true } }}><NativeOptions options={FREQUENCIES} /></TextField>
      </div>
      <TextField id="bugType" name="bugType" label={t('addBug.bugType', 'Bug type')} value={form.bugType} onChange={(event) => onFieldChange('bugType', event.target.value)} error={Boolean(errors.bugType)} helperText={errors.bugType || t('addBug.bugTypeHelp', 'Classify the primary failure mode.')} select fullWidth slotProps={{ select: { native: true } }}><NativeOptions options={BUG_TYPES} /></TextField>
    </>
  );
}

export function ProjectAndAssigneeFields({ form, errors, projects, projectsLoading, canCreateProject, canAssignTicket, isAdminHuman, newProjectName, newProjectNameError, newProjectVisibility, creatingProject, assignees, assigneesLoading, assigneesError, selectedProject, onProjectChange, onNewProjectNameChange, onNewProjectVisibilityChange, onCreateProject, onAssigneeChange }) {
  const { t } = useI18n();
  return (
    <>
      <TextField id="projectId" name="projectId" label={t('tickets.columns.project', 'Project')} value={form.projectId || ''} onChange={(event) => onProjectChange(event.target.value)} disabled={projectsLoading || (!canCreateProject && projects.length === 0)} error={Boolean(errors.projectId)} helperText={errors.projectId || (projectsLoading ? t('addBug.loadingProjects', 'Loading projects...') : t('addBug.projectHelp', 'Choose where this bug belongs.'))} select fullWidth slotProps={{ select: { native: true } }}>
        {projects.length === 0 && !canCreateProject ? <option value="">{t('addBug.noProjects', 'No projects available')}</option> : null}
        {projects.map((project) => <option key={project.projectId} value={project.projectId}>{project.name}{project.visibility === 'sensitive' ? ' (Sensitive)' : ' (Normal)'}</option>)}
        {canCreateProject ? <option value={ADD_PROJECT_OPTION}>{t('addBug.addProjectOption', '+ Add project...')}</option> : null}
      </TextField>
      {form.projectId === ADD_PROJECT_OPTION && canCreateProject ? (
        <Stack spacing={1.5}>
          <TextField id="newProjectName" name="newProjectName" label={t('addBug.newProjectName', 'New project name')} value={newProjectName} onChange={(event) => onNewProjectNameChange(event.target.value)} onKeyDown={(event) => { if (event.key === 'Enter' && !event.nativeEvent.isComposing) { event.preventDefault(); event.stopPropagation(); onCreateProject(event); } }} placeholder={t('addBug.newProjectNamePlaceholder', 'Project name (max 50 characters)')} error={Boolean(newProjectNameError)} helperText={newProjectNameError || t('addBug.newProjectNameHelp', 'Use a concise name, up to 50 characters.')} fullWidth slotProps={{ htmlInput: { maxLength: 50 } }} />
          {isAdminHuman ? (
            <TextField id="newProjectVisibility" label={t('addBug.newProjectVisibility', 'New project visibility')} value={newProjectVisibility} onChange={(event) => onNewProjectVisibilityChange(event.target.value)} helperText={t('addBug.sensitiveProjectHelp', 'Sensitive projects are visible only to explicitly allocated members.')} select fullWidth slotProps={{ select: { native: true } }}><option value="normal">Normal</option><option value="sensitive">Sensitive</option></TextField>
          ) : <Alert severity="info" role="status">Senior developers create normal projects.</Alert>}
          <Button type="button" onClick={onCreateProject} disabled={creatingProject} sx={{ alignSelf: { sm: 'flex-start' } }}>{creatingProject ? 'Creating...' : 'Add Project'}</Button>
        </Stack>
      ) : null}
      {canAssignTicket ? (
        <Stack spacing={1}>
          <TextField id="assigneeUserId" name="assigneeUserId" label={t('addBug.assignTicket', 'Assign ticket (optional)')} value={form.assigneeUserId} onChange={(event) => onAssigneeChange(event.target.value)} disabled={assigneesLoading || Boolean(assigneesError) || form.projectId === ADD_PROJECT_OPTION} helperText={assigneesLoading ? t('addBug.loadingAssignees', 'Loading assignment targets...') : selectedProject?.visibility === 'sensitive' ? t('addBug.sensitiveAssigneeHelp', 'Sensitive project: the selected assignee must already be a project member.') : t('addBug.unassignedHelp', 'Leave unassigned to create a todo ticket. Sensitive-project assignees must be project members.')} select fullWidth slotProps={{ select: { native: true } }}>
            <option value="" aria-label="No assignee" />
            {assignees.map((assignee) => <option key={assignee.userId} value={assignee.userId}>{formatUserIdentity(assignee)}</option>)}
          </TextField>
          {assigneesError ? <Alert severity="error" role="alert">{assigneesError}</Alert> : null}
        </Stack>
      ) : null}
    </>
  );
}

export function TicketMetadataAndEvidenceFields({ form, errors, selectedCoreTag, textEvidenceError, submitting, onFieldChange, onCoreTagChange, onRemoveEvidence, onAddEvidence }) {
  const { t } = useI18n();
  return (
    <>
      <TextField id="severity" name="severity" label={t('tickets.columns.severity', 'Severity')} value={form.severity} onChange={(event) => onFieldChange('severity', event.target.value)} error={Boolean(errors.severity)} helperText={errors.severity || t('addBug.severityHelp', 'How much user or system impact does this have?')} select fullWidth slotProps={{ select: { native: true } }}><NativeOptions options={SEVERITIES} /></TextField>
      <TextField id="priority" name="priority" label={t('tickets.columns.priority', 'Priority')} value={form.priority} onChange={(event) => onFieldChange('priority', event.target.value)} error={Boolean(errors.priority)} helperText={errors.priority || t('addBug.priorityHelp', 'Set the order this should be handled in.')} select fullWidth slotProps={{ select: { native: true } }}><NativeOptions options={PRIORITIES} /></TextField>
      <FormControl component="fieldset" className="tag-fieldset" error={Boolean(errors.tags)} required>
        <FormLabel component="legend" id="ticket-area-label">Ticket area</FormLabel>
        <FormHelperText id="ticket-area-help">Select exactly one area so the ticket routes to front-end or back-end ownership.</FormHelperText>
        <RadioGroup row aria-labelledby="ticket-area-label" aria-describedby="ticket-area-help" name="ticketArea" value={selectedCoreTag} onChange={(event) => onCoreTagChange(event.target.value)}>
          {CORE_TAGS.map((tag) => <FormControlLabel key={tag.value} value={tag.value} control={<Radio />} label={tag.label} />)}
        </RadioGroup>
        {errors.tags ? <FormHelperText role="alert">{errors.tags}</FormHelperText> : null}
      </FormControl>
      <fieldset className="tag-fieldset">
        <legend>Text Evidence</legend>
        <div className="evidence-list">
          {form.textEvidence.length === 0 ? <p className="evidence-empty">No text evidence attached.</p> : null}
          {form.textEvidence.map((evidence) => <div key={evidence.name} className="evidence-row"><span>{evidence.name}</span><button type="button" className="tiny-action" onClick={() => onRemoveEvidence(evidence.name)}>Remove</button></div>)}
        </div>
        <label className="tiny-upload-label">Attach .txt file<input className="tiny-upload-input" type="file" accept="text/plain,.txt" onChange={onAddEvidence} disabled={submitting || form.textEvidence.length >= MAX_TEXT_EVIDENCE_FILES} /></label>
        {textEvidenceError ? <p role="alert" className="error-text">{textEvidenceError}</p> : null}
      </fieldset>
    </>
  );
}
