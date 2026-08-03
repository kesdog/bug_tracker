import React, { useEffect, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Typography from '@mui/material/Typography';
import { BugReportFields, ProjectAndAssigneeFields, TicketMetadataAndEvidenceFields } from '../components/AddBugFormSections';
import { createBug, fetchAssignableUsers } from '../api/bugs';
import { createProject, fetchProjects } from '../api/projects';
import { ADD_PROJECT_OPTION, CORE_TAG_VALUES, MAX_TEXT_EVIDENCE_FILES, buildCreatePayload, createInitialBugForm, fileToTextEvidence, validateForm } from '../add_bug_form';
import { MAX_REPORT_TEXT_LENGTH, ReportBuilder } from '../report_builder';

export default function AddBugPage({ token, userRole, userType, onCreated }) {
  const isHuman = userType !== 'agent';
  const canCreateProject = isHuman && (userRole === 'senior' || userRole === 'admin');
  const canAssignTicket = isHuman && (userRole === 'senior' || userRole === 'admin');
  const [form, setForm] = useState(() => createInitialBugForm());
  const [projects, setProjects] = useState([]);
  const [projectsLoading, setProjectsLoading] = useState(false);
  const [newProjectName, setNewProjectName] = useState('');
  const [newProjectNameError, setNewProjectNameError] = useState('');
  const [newProjectVisibility, setNewProjectVisibility] = useState('normal');
  const [creatingProject, setCreatingProject] = useState(false);
  const [assignees, setAssignees] = useState([]);
  const [assigneesLoading, setAssigneesLoading] = useState(false);
  const [assigneesError, setAssigneesError] = useState('');
  const [reportBuilder, setReportBuilder] = useState(() => ReportBuilder.fromSerialized('', []));
  const [reportBuilderError, setReportBuilderError] = useState('');
  const [textEvidenceError, setTextEvidenceError] = useState('');
  const [errors, setErrors] = useState({});
  const [submitError, setSubmitError] = useState('');
  const [successMessage, setSuccessMessage] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const selectedCoreTag = CORE_TAG_VALUES.find((tag) => form.tags.includes(tag)) || '';
  const selectedProject = projects.find((project) => project.projectId === form.projectId) || null;

  // Applies either a direct builder instance or a functional builder update.
  function applyReportBuilder(nextValueOrUpdater) {
    setReportBuilder((current) => (typeof nextValueOrUpdater === 'function' ? nextValueOrUpdater(current) : nextValueOrUpdater));
  }

  // Generic field updater for simple string/select controls.
  function updateField(key, value) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  function updateProject(projectId) {
    setForm((current) => ({ ...current, projectId, assigneeUserId: '' }));
    setNewProjectNameError('');
  }

  function updateCoreTag(tag) {
    setForm((current) => {
      const tags = Array.isArray(current.tags) ? current.tags : [];
      return {
        ...current,
        tags: tag
          ? [...tags.filter((value) => !CORE_TAG_VALUES.includes(value)), tag]
          : tags.filter((value) => !CORE_TAG_VALUES.includes(value))
      };
    });
  }

  async function addTextEvidence(event) {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) {
      return;
    }

    if (form.textEvidence.length >= MAX_TEXT_EVIDENCE_FILES) {
      setTextEvidenceError(`Attach up to ${MAX_TEXT_EVIDENCE_FILES} text evidence files.`);
      return;
    }

    try {
      const evidence = await fileToTextEvidence(file);
      setForm((current) => ({
        ...current,
        textEvidence: [...current.textEvidence, evidence]
      }));
      setTextEvidenceError('');
    } catch (err) {
      setTextEvidenceError(err.message || 'Unable to attach text evidence.');
    }
  }

  function removeTextEvidence(name) {
    setForm((current) => ({
      ...current,
      textEvidence: current.textEvidence.filter((evidence) => evidence.name !== name)
    }));
  }

  useEffect(() => {
    let active = true;

    async function loadProjects() {
      setProjectsLoading(true);
      setSubmitError('');
      try {
        const result = await fetchProjects(token);
        if (!active) {
          return;
        }

        const projectList = Array.isArray(result) ? result : [];
        setProjects(projectList);
        setForm((current) => ({
          ...current,
          projectId: current.projectId || projectList[0]?.projectId || (canCreateProject ? ADD_PROJECT_OPTION : '')
        }));
      } catch (err) {
        if (active) {
          setProjects([]);
          setSubmitError(err.message || 'Unable to load projects.');
        }
      } finally {
        if (active) {
          setProjectsLoading(false);
        }
      }
    }

    loadProjects();

    return () => {
      active = false;
    };
  }, [canCreateProject, token]);

  useEffect(() => {
    if (!canAssignTicket) {
      setAssignees([]);
      setAssigneesError('');
      return undefined;
    }

    let active = true;
    setAssigneesLoading(true);
    setAssigneesError('');

    fetchAssignableUsers(token)
      .then((result) => {
        if (active) {
          setAssignees(Array.isArray(result) ? result : []);
        }
      })
      .catch((err) => {
        if (active) {
          setAssignees([]);
          setAssigneesError(err.message || 'Unable to load assignment targets.');
        }
      })
      .finally(() => {
        if (active) {
          setAssigneesLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [canAssignTicket, token]);

  async function handleCreateProject(event) {
    event?.preventDefault();
    if (!canCreateProject) {
      return;
    }

    const trimmed = newProjectName.trim();
    if (!trimmed) {
      setNewProjectNameError('Project name is required.');
      return;
    }

    if (trimmed.length > 50) {
      setNewProjectNameError('Project name must be 50 characters or less.');
      return;
    }

    setNewProjectNameError('');
    setSubmitError('');
    setCreatingProject(true);
    try {
      const visibility = isHuman && userRole === 'admin' ? newProjectVisibility : 'normal';
      const created = await createProject(token, trimmed, visibility);
      setProjects((current) => [...current, { ...created, visibility: created.visibility || visibility }].sort((a, b) => a.name.localeCompare(b.name)));
      setNewProjectName('');
      setNewProjectNameError('');
      setNewProjectVisibility('normal');
      setForm((current) => ({ ...current, projectId: created.projectId, assigneeUserId: '' }));
      setSuccessMessage(`Project created: ${created.name}`);
    } catch (err) {
      setSubmitError(err.message || 'Unable to create project.');
    } finally {
      setCreatingProject(false);
    }
  }

  // Handles full form submit and creates the bug ticket.
  async function handleSubmit(event) {
    event.preventDefault();
    setSubmitError('');
    setSuccessMessage('');

    const reportPayload = reportBuilder.toPayload();
    if (reportBuilder.textLength > MAX_REPORT_TEXT_LENGTH) {
      setReportBuilderError(`Report text must be ${MAX_REPORT_TEXT_LENGTH.toLocaleString()} characters or less.`);
      return;
    }
    const nextForm = {
      ...form,
      description: reportPayload.text
    };

    const nextErrors = validateForm(nextForm);
    setErrors(nextErrors);
    if (Object.keys(nextErrors).length > 0) {
      return;
    }

    setSubmitting(true);
    try {
      const created = await createBug(token, buildCreatePayload(form, reportBuilder));

      setSuccessMessage(`Bug created: ${created.id}`);
      setForm((current) => createInitialBugForm(current.projectId));
      setReportBuilder(ReportBuilder.fromSerialized('', []));
      setReportBuilderError('');
      setTextEvidenceError('');
      setErrors({});
      if (onCreated) {
        onCreated();
      }
    } catch (err) {
      setSubmitError(err.message || 'Unable to create bug.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <section className="dashboard">
      <Typography component="h2" variant="h4">Add Bug</Typography>
      <Typography className="subtitle">Capture a new issue with report notes, images, and project metadata.</Typography>

      <Card className="add-bug-card">
        <CardContent>
          <Box component="form" className="add-bug-form" onSubmit={handleSubmit} noValidate>
        <BugReportFields form={form} errors={errors} reportBuilder={reportBuilder} reportBuilderError={reportBuilderError} submitting={submitting} onFieldChange={updateField} onBuilderChange={applyReportBuilder} onBuilderError={setReportBuilderError} />
        <ProjectAndAssigneeFields
          form={form} errors={errors} projects={projects} projectsLoading={projectsLoading} canCreateProject={canCreateProject} canAssignTicket={canAssignTicket}
          isAdminHuman={isHuman && userRole === 'admin'} newProjectName={newProjectName} newProjectNameError={newProjectNameError} newProjectVisibility={newProjectVisibility}
          creatingProject={creatingProject} assignees={assignees} assigneesLoading={assigneesLoading} assigneesError={assigneesError} selectedProject={selectedProject}
          onProjectChange={updateProject} onNewProjectNameChange={(value) => { setNewProjectName(value); setNewProjectNameError(''); }} onNewProjectVisibilityChange={setNewProjectVisibility}
          onCreateProject={handleCreateProject} onAssigneeChange={(value) => updateField('assigneeUserId', value)}
        />
        <TicketMetadataAndEvidenceFields form={form} errors={errors} selectedCoreTag={selectedCoreTag} textEvidenceError={textEvidenceError} submitting={submitting} onFieldChange={updateField} onCoreTagChange={updateCoreTag} onRemoveEvidence={removeTextEvidence} onAddEvidence={addTextEvidence} />

        <Button type="submit" disabled={submitting} size="large">
          {submitting ? 'Creating...' : 'Create Bug'}
        </Button>
          </Box>
        </CardContent>
      </Card>

      {submitError ? <Alert severity="error" role="alert">{submitError}</Alert> : null}
      {successMessage ? <Alert severity="success">{successMessage}</Alert> : null}
    </section>
  );
}
