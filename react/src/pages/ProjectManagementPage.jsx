import React, { useEffect, useMemo, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import PublicOutlinedIcon from '@mui/icons-material/PublicOutlined';
import {
  createProject,
  fetchAllocatableProjectUsers,
  fetchProjectAllocations,
  fetchProjects,
  updateProjectAllocations,
  updateProjectVisibility
} from '../api/projects';
import { LoadingState } from '../components/MuiPrimitives';
import ProjectAllocationsCard from '../components/ProjectAllocationsCard';
import { formatUserIdentity } from '../user_identity';
import { useI18n } from '../i18n';

const ADD_PROJECT_OPTION = '__add_project__';

function normalizeVisibility(value) {
  return value === 'sensitive' ? 'sensitive' : 'normal';
}

function visibilityLabel(value, t) {
  return normalizeVisibility(value) === 'sensitive' ? t('pages.projectManagement.sensitive', 'Sensitive') : t('pages.projectManagement.normal', 'Normal');
}

function formatProjectUser(user) {
  if (!user) {
    return '';
  }

  return formatUserIdentity(user);
}

export default function ProjectManagementPage({ token, userRole, userType }) {
  const { t } = useI18n();
  const isAdmin = userType !== 'agent' && userRole === 'admin';
  const [projects, setProjects] = useState([]);
  const [users, setUsers] = useState([]);
  const [allocationsByProject, setAllocationsByProject] = useState({});
  const [selectedProjectId, setSelectedProjectId] = useState('');
  const [selectedUserId, setSelectedUserId] = useState('');
  const [newProjectName, setNewProjectName] = useState('');
  const [newProjectNameError, setNewProjectNameError] = useState('');
  const [newProjectVisibility, setNewProjectVisibility] = useState('normal');
  const [visibilityDraft, setVisibilityDraft] = useState('normal');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [creatingProject, setCreatingProject] = useState(false);
  const [savingVisibility, setSavingVisibility] = useState(false);
  const [error, setError] = useState('');
  const [successMessage, setSuccessMessage] = useState('');

  const selectedProject = useMemo(
    () => projects.find((project) => project.projectId === selectedProjectId) || null,
    [projects, selectedProjectId]
  );
  const selectedVisibility = normalizeVisibility(selectedProject?.visibility);
  const canManageSelectedAllocations = isAdmin || (
    userType !== 'agent' && userRole === 'senior' && selectedVisibility === 'normal'
  );
  const selectedProjectUserIds = selectedProject ? allocationsByProject[selectedProject.projectId] || [] : [];
  const ownerIdentity = selectedProject?.owner || selectedProject?.ownerIdentity || (selectedProject?.ownerUserId ? users.find((user) => user.userId === selectedProject.ownerUserId) || { userId: selectedProject.ownerUserId } : null);
  const selectedProjectUserIdSet = useMemo(
    () => new Set(selectedProjectUserIds),
    [selectedProjectUserIds]
  );
  const associatedUsers = useMemo(
    () => selectedProjectUserIds
      .map((userId) => users.find((user) => user.userId === userId) || { userId, role: 'unknown', userType: 'human' }),
    [selectedProjectUserIds, users]
  );
  const availableUsers = useMemo(
    () => users.filter((user) => !selectedProjectUserIdSet.has(user.userId)),
    [selectedProjectUserIdSet, users]
  );

  useEffect(() => {
    let active = true;

    async function load() {
      setLoading(true);
      setError('');
      setSuccessMessage('');

      try {
        const [nextProjects, nextUsers, nextAllocations] = await Promise.all([
          fetchProjects(token),
          fetchAllocatableProjectUsers(token),
          fetchProjectAllocations(token)
        ]);

        if (!active) {
          return;
        }

        const normalizedProjects = (Array.isArray(nextProjects) ? nextProjects : []).map((project) => ({
          ...project,
          visibility: normalizeVisibility(project.visibility)
        }));
        const normalizedUsers = Array.isArray(nextUsers) ? nextUsers : [];
        const allocationMap = {};

        (Array.isArray(nextAllocations) ? nextAllocations : []).forEach((entry) => {
          allocationMap[entry.projectId] = Array.isArray(entry.userIds) ? entry.userIds : [];
        });

        setProjects(normalizedProjects);
        setUsers(normalizedUsers);
        setAllocationsByProject(allocationMap);
        setSelectedProjectId((current) => current || normalizedProjects[0]?.projectId || '');
        setSelectedUserId((current) => current || normalizedUsers[0]?.userId || '');
      } catch (err) {
        if (active) {
           setError(err.message || t('pages.projectManagement.errors.load', 'Unable to load project management data.'));
          setProjects([]);
          setUsers([]);
          setAllocationsByProject({});
          setSelectedProjectId('');
          setSelectedUserId('');
        }
      } finally {
        if (active) {
          setLoading(false);
        }
      }
    }

    load();
    return () => {
      active = false;
    };
  }, [token]);

  useEffect(() => {
    setSelectedUserId((current) => {
      if (current && availableUsers.some((user) => user.userId === current)) {
        return current;
      }

      return availableUsers[0]?.userId || '';
    });
  }, [availableUsers]);

  useEffect(() => {
    setVisibilityDraft(selectedVisibility);
  }, [selectedProjectId, selectedVisibility]);

  async function handleCreateProject(event) {
    event.preventDefault();
    const trimmed = newProjectName.trim();
    if (!trimmed) {
       setNewProjectNameError(t('pages.projectManagement.errors.nameRequired', 'Project name is required.'));
      return;
    }

    if (trimmed.length > 50) {
       setNewProjectNameError(t('pages.projectManagement.errors.nameLength', 'Project name must be 50 characters or less.'));
      return;
    }

    const visibility = isAdmin ? newProjectVisibility : 'normal';
    setCreatingProject(true);
    setNewProjectNameError('');
    setError('');
    setSuccessMessage('');
    try {
      const created = await createProject(token, trimmed, visibility);
      const normalizedCreated = { ...created, visibility: normalizeVisibility(created.visibility || visibility) };
      const nextProjects = [...projects, normalizedCreated].sort((a, b) => a.name.localeCompare(b.name));
      setProjects(nextProjects);
      setAllocationsByProject((current) => ({ ...current, [created.projectId]: [] }));
      setSelectedProjectId(created.projectId);
      setNewProjectName('');
      setNewProjectNameError('');
      setNewProjectVisibility('normal');
       setSuccessMessage(`${t('pages.projectManagement.createdProject', 'Created')} ${visibilityLabel(visibility, t).toLowerCase()} ${t('pages.projectManagement.project', 'project')}: ${created.name}`);
    } catch (err) {
       setError(err.message || t('pages.projectManagement.errors.create', 'Unable to create project.'));
    } finally {
      setCreatingProject(false);
    }
  }

  async function handleUpdateVisibility() {
    if (!isAdmin || !selectedProject || visibilityDraft === selectedVisibility) {
      return;
    }

    setSavingVisibility(true);
    setError('');
    setSuccessMessage('');
    try {
      const updated = await updateProjectVisibility(token, selectedProject.projectId, visibilityDraft);
      const visibility = normalizeVisibility(updated?.visibility || visibilityDraft);
      setProjects((current) => current.map((project) => (
        project.projectId === selectedProject.projectId
          ? { ...project, ...updated, visibility }
          : project
      )));
       setSuccessMessage(`${selectedProject.name} ${t('pages.projectManagement.isNow', 'is now')} ${visibilityLabel(visibility, t).toLowerCase()}.`);
    } catch (err) {
       setError(err.message || t('pages.projectManagement.errors.updateVisibility', 'Unable to update project visibility.'));
    } finally {
      setSavingVisibility(false);
    }
  }

  async function handleAllocateUser() {
    if (!canManageSelectedAllocations) {
      return;
    }

    if (!selectedProject || !selectedUserId) {
       setError(t('pages.projectManagement.errors.selectProjectAndUser', 'Select both a project and a user first.'));
      return;
    }

    setSaving(true);
    setError('');
    setSuccessMessage('');
    try {
      const currentUserIds = selectedProjectUserIds;
      if (currentUserIds.includes(selectedUserId)) {
         setError(t('pages.projectManagement.errors.userAlreadyAllocated', 'This user is already allocated to that project.'));
        return;
      }

      const nextUserIds = [...currentUserIds, selectedUserId];
      await updateProjectAllocations(token, selectedProject.projectId, nextUserIds);
      setAllocationsByProject((current) => ({
        ...current,
        [selectedProject.projectId]: nextUserIds
      }));
       setSuccessMessage(t('pages.projectManagement.userAllocated', 'User allocated to project.'));
    } catch (err) {
       setError(err.message || t('pages.projectManagement.errors.allocateUser', 'Unable to allocate user to project.'));
    } finally {
      setSaving(false);
    }
  }

  const controlsDisabled = saving || creatingProject || savingVisibility;

  return (
    <Box component="section" className="dashboard">
       <Typography component="h2" variant="h4">{t('pages.projectManagement.title', 'Project Management')}</Typography>
      <Typography color="text.secondary" sx={{ mb: 2.5 }}>
         {t('pages.projectManagement.subtitle', 'Create projects, review visibility, and manage project membership.')}
      </Typography>

      <Stack spacing={2}>
        {error ? <Alert severity="error" role="alert">{error}</Alert> : null}
        {successMessage ? <Alert severity="success" role="status">{successMessage}</Alert> : null}
         {loading ? <LoadingState label={t('pages.projectManagement.loading', 'loading projects')} /> : null}

        {!loading ? (
          <Stack spacing={2.5}>
            <Card variant="outlined">
              <CardContent>
                <Stack spacing={2}>
                  <Box>
                     <Typography component="h3" variant="h6">{t('pages.projectManagement.project', 'Project')}</Typography>
                     <Typography variant="body2" color="text.secondary">{t('pages.projectManagement.selectProjectHelp', 'Select a project to review access and settings.')}</Typography>
                  </Box>
                  <TextField
                    id="projectSelect"
                     label={t('pages.projectManagement.project', 'Project')}
                    value={selectedProjectId}
                    onChange={(event) => setSelectedProjectId(event.target.value)}
                    disabled={controlsDisabled}
                    select
                    fullWidth
                    slotProps={{ select: { native: true } }}
                  >
                    {projects.map((project) => (
                      <option key={project.projectId} value={project.projectId}>
                         {project.name} ({visibilityLabel(project.visibility, t)})
                      </option>
                    ))}
                     <option value={ADD_PROJECT_OPTION}>{t('pages.projectManagement.createNewProjectOption', '+ Create new project...')}</option>
                  </TextField>

                  {selectedProjectId === ADD_PROJECT_OPTION ? (
                    <Stack component="form" onSubmit={handleCreateProject} spacing={2}>
                      <Divider />
                       <Typography component="h3" variant="h6">{t('pages.projectManagement.createProject', 'Create project')}</Typography>
                      <TextField
                        id="newProjectName"
                         label={t('pages.projectManagement.newProjectName', 'New project name')}
                        value={newProjectName}
                        onChange={(event) => {
                          setNewProjectName(event.target.value);
                          setNewProjectNameError('');
                        }}
                         placeholder={t('pages.projectManagement.namePlaceholder', 'Project name (max 50 characters)')}
                        error={Boolean(newProjectNameError)}
                         helperText={newProjectNameError || t('pages.projectManagement.nameHelp', 'Use a concise name, up to 50 characters.')}
                        slotProps={{ htmlInput: { maxLength: 50 } }}
                        fullWidth
                      />
                      {isAdmin ? (
                        <TextField
                          id="newProjectVisibility"
                          label="Project visibility"
                          value={newProjectVisibility}
                          onChange={(event) => setNewProjectVisibility(event.target.value)}
                          helperText="Sensitive projects are visible only to explicitly allocated members."
                          select
                          fullWidth
                          slotProps={{ select: { native: true } }}
                        >
                          <option value="normal">Normal</option>
                          <option value="sensitive">Sensitive</option>
                        </TextField>
                      ) : (
                        <Alert severity="info" role="status">Senior developers create normal projects. An admin can change visibility later.</Alert>
                      )}
                      {isAdmin && newProjectVisibility === 'sensitive' ? (
                        <Alert severity="warning">
                          Sensitive projects are membership-only. Allocate every user who needs to discover or work in this project.
                        </Alert>
                      ) : null}
                      <Button type="submit" disabled={creatingProject} sx={{ alignSelf: { sm: 'flex-start' } }}>
                         {creatingProject ? t('common.creating', 'Creating...') : t('pages.projectManagement.createProject', 'Create Project')}
                      </Button>
                    </Stack>
                  ) : selectedProject ? (
                    <Stack spacing={2.5}>
                      <Divider />
                      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ alignItems: { xs: 'flex-start', sm: 'center' }, justifyContent: 'space-between' }}>
                         <Box>
                           <Typography component="h3" variant="h6">{selectedProject.name}</Typography>
                            <Typography variant="body2" sx={{ fontWeight: 700 }}>{t('pages.projectManagement.owner', 'Owner')}: {ownerIdentity ? formatProjectUser(ownerIdentity) : t('common.notProvided', 'Not provided')}</Typography>
                          <Typography variant="body2" color="text.secondary">
                            {selectedVisibility === 'sensitive'
                              ? 'Only explicitly allocated members can discover and work in this project.'
                              : 'Normal project visibility follows the user’s role and allocation scope.'}
                          </Typography>
                        </Box>
                        <Chip
                          icon={selectedVisibility === 'sensitive' ? <LockOutlinedIcon /> : <PublicOutlinedIcon />}
                           label={`${visibilityLabel(selectedVisibility, t)} ${t('pages.projectManagement.project', 'project')}`}
                          color={selectedVisibility === 'sensitive' ? 'warning' : 'success'}
                          variant="outlined"
                        />
                      </Stack>

                      {isAdmin ? (
                        <Box sx={{ p: { xs: 2, sm: 2.5 }, border: 1, borderColor: 'divider', borderRadius: 2, bgcolor: 'action.hover' }}>
                          <Stack spacing={1.5}>
                            <Box>
                              <Typography component="h4" variant="subtitle1" sx={{ fontWeight: 800 }}>Visibility settings</Typography>
                              <Typography variant="body2" color="text.secondary">
                                Changing visibility affects who can discover this project. Sensitive projects require explicit membership for every assignee.
                              </Typography>
                            </Box>
                            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ alignItems: { xs: 'stretch', sm: 'flex-start' } }}>
                              <TextField
                                id="projectVisibility"
                                label="Project visibility"
                                value={visibilityDraft}
                                onChange={(event) => setVisibilityDraft(event.target.value)}
                                disabled={savingVisibility}
                                select
                                fullWidth
                                slotProps={{ select: { native: true } }}
                              >
                                <option value="normal">Normal</option>
                                <option value="sensitive">Sensitive</option>
                              </TextField>
                              <Button
                                type="button"
                                onClick={handleUpdateVisibility}
                                disabled={savingVisibility || visibilityDraft === selectedVisibility}
                                sx={{ minWidth: { sm: 172 }, minHeight: 56 }}
                              >
                                {savingVisibility ? 'Saving...' : 'Save Visibility'}
                              </Button>
                            </Stack>
                            {visibilityDraft !== selectedVisibility ? (
                              <Alert severity="warning">
                                {visibilityDraft === 'sensitive'
                                  ? 'Before saving, confirm that all current ticket assignees are allocated to this project.'
                                  : 'Normal visibility broadens project discovery according to role-based access.'}
                              </Alert>
                            ) : null}
                          </Stack>
                        </Box>
                      ) : (
                        <Typography variant="body2" color="text.secondary">Only admins can change project visibility.</Typography>
                      )}
                    </Stack>
                  ) : (
                    <Alert severity="info">Create a project to begin managing allocations.</Alert>
                  )}
                </Stack>
              </CardContent>
            </Card>

            {selectedProject ? (
              <ProjectAllocationsCard
                associatedUsers={associatedUsers}
                availableUsers={availableUsers}
                canManage={canManageSelectedAllocations}
                controlsDisabled={controlsDisabled}
                ownerUserId={ownerIdentity?.userId}
                saving={saving}
                selectedUserId={selectedUserId}
                visibility={selectedVisibility}
                onAllocate={handleAllocateUser}
                onSelectedUserIdChange={setSelectedUserId}
              />
            ) : null}
          </Stack>
        ) : null}
      </Stack>
    </Box>
  );
}
