import React, { useEffect, useMemo, useState } from 'react';
import Alert from '@mui/material/Alert';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Tab from '@mui/material/Tab';
import Tabs from '@mui/material/Tabs';
import Typography from '@mui/material/Typography';
import { createRequest, fetchRequests, fetchUsers, issueAgentApiKey, issuePasswordReset, issueRecoveryAgentApiKey, issueSetupLink, reissueAgentApiKey, removeRequest, updateRequestUsername, updateUserUsername } from '../api/users';
import { AccessRequestCard, RequestsGridCard, UsersGridCard } from '../components/UserManagementCards';
import { ApiKeyDialog, EditUsernameDialog, PasswordLinkDialog } from '../components/UserManagementDialogs';
import { isValidEmail } from '../user_management_utils';

export default function UserManagementPage({ token, onViewUserLogs, onViewUserTickets, onViewUserSubmitted }) {
  const [users, setUsers] = useState([]);
  const [requests, setRequests] = useState([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [successMessage, setSuccessMessage] = useState('');
  const [requestType, setRequestType] = useState('human');
  const [email, setEmail] = useState('');
  const [emailConfirm, setEmailConfirm] = useState('');
  const [managementTab, setManagementTab] = useState('users');
  const [activeTab, setActiveTab] = useState('human');
  const [menuState, setMenuState] = useState(null);
  const [userMenuState, setUserMenuState] = useState(null);
  const [editUsernameRequest, setEditUsernameRequest] = useState(null);
  const [usernameInput, setUsernameInput] = useState('');
  const [apiKeyRequest, setApiKeyRequest] = useState(null);
  const [apiKeyActiveDays, setApiKeyActiveDays] = useState('30');
  const [generatedApiKey, setGeneratedApiKey] = useState(null);
  const [copyMessage, setCopyMessage] = useState('');
  const [generatedSetupLink, setGeneratedSetupLink] = useState(null);

  const humanRequests = useMemo(() => requests.filter((request) => request.requestType === 'human'), [requests]);
  const aiRequests = useMemo(() => requests.filter((request) => request.requestType === 'ai_agent'), [requests]);

  useEffect(() => {
    if (activeTab === 'human' && humanRequests.length === 0 && aiRequests.length > 0) {
      setActiveTab('ai_agent');
    }
  }, [activeTab, aiRequests.length, humanRequests.length]);

  const currentRows = activeTab === 'human' ? humanRequests : aiRequests;
  const gridRows = useMemo(() => currentRows.map((request) => ({ id: request.requestId, ...request })), [currentRows]);
  const userRows = useMemo(() => users.map((user) => ({ id: user.userId, ...user })), [users]);

  async function loadAll() {
    setLoading(true);
    setError('');
    try {
      const [nextUsers, nextRequests] = await Promise.all([
        fetchUsers(token),
        fetchRequests(token)
      ]);
      setUsers(Array.isArray(nextUsers) ? nextUsers : []);
      setRequests(Array.isArray(nextRequests) ? nextRequests : []);
    } catch (err) {
      setError(err.message || 'Unable to load user management data.');
      setUsers([]);
      setRequests([]);
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    loadAll();
  }, [token]);

  function openUserContextMenu(user, x, y) {
    setUserMenuState({ user, x, y });
  }

  function runUserAction(action) {
    const user = userMenuState?.user;
    setUserMenuState(null);
    if (!user) {
      return;
    }

    if (action === 'logs') {
      onViewUserLogs?.(user);
    } else if (action === 'active') {
      onViewUserTickets?.(user, 'active');
    } else if (action === 'solved') {
      onViewUserTickets?.(user, 'closed');
    } else if (action === 'submitted') {
      onViewUserSubmitted?.(user);
    } else if (action === 'api-key') {
      setApiKeyRequest({
        userId: user.userId,
        username: user.username || user.userId,
        email: user.email,
        requestType: 'ai_agent'
      });
      setApiKeyActiveDays('30');
      setGeneratedApiKey(null);
      setCopyMessage('');
    } else if (action === 'edit-username') {
      setEditUsernameRequest({ ...user, targetType: 'user' });
      setUsernameInput(user.username || '');
    }
  }

  function openContextMenu(request, x, y) {
    setMenuState({ request, x, y });
  }

  async function submitRequest(event) {
    event.preventDefault();
    const normalizedEmail = email.trim().toLowerCase();
    const normalizedConfirm = emailConfirm.trim().toLowerCase();

    setError('');
    setSuccessMessage('');

    if (!normalizedEmail || !normalizedConfirm) {
      setError('Email and confirm email are required.');
      return;
    }

    if (normalizedEmail !== normalizedConfirm) {
      setError('Email and confirm email must match.');
      return;
    }

    if (!isValidEmail(normalizedEmail)) {
      setError('Enter a valid email address.');
      return;
    }

    setSaving(true);
    try {
      const created = await createRequest(token, normalizedEmail, requestType);
      setRequests((current) => [created, ...current]);
      setEmail('');
      setEmailConfirm('');
      setActiveTab(created.requestType === 'ai_agent' ? 'ai_agent' : 'human');
      setSuccessMessage('Request created.');
    } catch (err) {
      setError(err.message || 'Unable to create request.');
    } finally {
      setSaving(false);
    }
  }

  async function runAction(action) {
    if (!menuState?.request) {
      return;
    }

    const request = menuState.request;
    setMenuState(null);
    setError('');
    setSuccessMessage('');

    try {
      if (action === 'edit-username') {
        setEditUsernameRequest({ ...request, targetType: 'request' });
        setUsernameInput(request.username || '');
        return;
      }

      setSaving(true);

      if (action === 'setup-link') {
        const result = await issueSetupLink(token, request.requestId);
        setGeneratedSetupLink({ link: result.link || '', email: request.email, expiresAt: result.expiresAt || '' });
        setCopyMessage('');
        setSuccessMessage('Setup link generated. Copy or email it from the dialog.');
      } else if (action === 'password-reset') {
        const result = await issuePasswordReset(token, request.requestId.replace(/^recovery_/, ''));
        setGeneratedSetupLink({ link: result.link || '', email: request.email, expiresAt: result.expiresAt || '' });
        setCopyMessage('');
        setSuccessMessage('Password reset link generated. Copy or email it from the dialog.');
      } else if (action === 'api-key') {
        setApiKeyRequest(request);
        setApiKeyActiveDays('30');
        setGeneratedApiKey(null);
        setCopyMessage('');
        return;
      } else if (action === 'remove') {
        await removeRequest(token, request.requestId);
        setRequests((current) => current.filter((item) => item.requestId !== request.requestId));
        setSuccessMessage('Request removed.');
      }

      await loadAll();
    } catch (err) {
      setError(err.message || 'Action failed.');
    } finally {
      setSaving(false);
    }
  }

  function closeApiKeyDialog() {
    setApiKeyRequest(null);
    setGeneratedApiKey(null);
    setApiKeyActiveDays('30');
    setCopyMessage('');
  }

  async function generateAgentApiKey() {
    if (!apiKeyRequest) {
      return;
    }

    const activeDays = Number.parseInt(apiKeyActiveDays, 10);
    if (!Number.isInteger(activeDays) || activeDays < 1 || activeDays > 62) {
      setError('Active days must be between 1 and 62.');
      return;
    }

    setSaving(true);
    setError('');
    setSuccessMessage('');
    setCopyMessage('');
    try {
      const result = apiKeyRequest.recoveryId
        ? await issueRecoveryAgentApiKey(token, apiKeyRequest.recoveryId, activeDays)
        : apiKeyRequest.requestId
          ? await issueAgentApiKey(token, apiKeyRequest.requestId, activeDays)
        : await reissueAgentApiKey(token, apiKeyRequest.userId, activeDays);
      setGeneratedApiKey({
        apiKey: result.apiKey || '',
        username: result.username || apiKeyRequest.username || apiKeyRequest.userId || '',
        email: apiKeyRequest.email,
        expiresAt: result.expiresAt || ''
      });
      setSuccessMessage(apiKeyRequest.requestId ? 'AI oath token generated. It will only be shown in this dialog.' : 'AI oath token reissued. It will only be shown in this dialog.');
      await loadAll();
    } catch (err) {
      setError(err.message || 'Unable to generate AI oath token.');
    } finally {
      setSaving(false);
    }
  }

  async function copyGeneratedApiKey() {
    if (!generatedApiKey?.apiKey) {
      return;
    }

    try {
      const clipboard = globalThis.navigator?.clipboard || window.navigator?.clipboard;
      if (!clipboard?.writeText) {
        throw new Error('clipboard unavailable');
      }

      await clipboard.writeText(generatedApiKey.apiKey);
      setCopyMessage('Copied oath token.');
    } catch {
      setCopyMessage('Copy failed. Select the token and copy it manually.');
    }
  }

  async function copyGeneratedSetupLink() {
    if (!generatedSetupLink?.link) return;
    try {
      const clipboard = globalThis.navigator?.clipboard || window.navigator?.clipboard;
      if (!clipboard?.writeText) {
        throw new Error('clipboard unavailable');
      }
      await clipboard.writeText(generatedSetupLink.link);
      setCopyMessage('Copied password link.');
    } catch {
      setCopyMessage('Copy failed. Select the link and copy it manually.');
    }
  }

  async function saveUsername() {
    if (!editUsernameRequest) {
      return;
    }

    const value = usernameInput.trim();
    if (!value) {
      setError('Username is required.');
      return;
    }

    setSaving(true);
    setError('');
    try {
      if (editUsernameRequest.targetType === 'user') {
        const updated = await updateUserUsername(token, editUsernameRequest.userId, value);
        setUsers((current) => current.map((user) => (user.userId === updated.userId ? { ...user, ...updated } : user)));
      } else {
        const updated = await updateRequestUsername(token, editUsernameRequest.requestId, value);
        setRequests((current) => current.map((request) => (request.requestId === updated.requestId ? updated : request)));
      }
      setEditUsernameRequest(null);
      setUsernameInput('');
      setSuccessMessage('Username updated.');
    } catch (err) {
      setError(err.message || 'Unable to update username.');
    } finally {
      setSaving(false);
    }
  }

  const menuRequest = menuState?.request;

  return (
    <section className="dashboard">
      <Typography component="h2" variant="h4" sx={{ fontWeight: 900 }}>Users</Typography>
      <Typography className="subtitle" color="text.secondary">Manage users, review access requests, and jump into user-scoped activity.</Typography>

      <Tabs value={managementTab} onChange={(event, value) => setManagementTab(value)} aria-label="User management tabs" sx={{ mt: 2 }}>
        <Tab value="users" label={`Users (${users.length})`} />
        <Tab value="requests" label={`Requests (${requests.length})`} />
      </Tabs>

      {managementTab === 'requests' ? (
        <AccessRequestCard
          requestType={requestType}
          email={email}
          emailConfirm={emailConfirm}
          saving={saving}
          onRequestTypeChange={setRequestType}
          onEmailChange={setEmail}
          onEmailConfirmChange={setEmailConfirm}
          onSubmit={submitRequest}
        />
      ) : null}

      {error ? <Alert severity="error" role="alert" sx={{ my: 1 }}>{error}</Alert> : null}
      {successMessage ? <Alert severity="success" sx={{ my: 1 }}>{successMessage}</Alert> : null}

      {managementTab === 'users' ? (
        <UsersGridCard
          loading={loading}
          userRows={userRows}
          onOpenMenu={openUserContextMenu}
        />
      ) : null}

      {managementTab === 'requests' ? (
        <RequestsGridCard
          loading={loading}
          gridRows={gridRows}
          activeTab={activeTab}
          humanCount={humanRequests.length}
          aiCount={aiRequests.length}
          onActiveTabChange={setActiveTab}
          onOpenMenu={openContextMenu}
        />
      ) : null}

      <Menu
        open={Boolean(userMenuState)}
        onClose={() => setUserMenuState(null)}
        anchorReference="anchorPosition"
        anchorPosition={userMenuState ? { top: userMenuState.y, left: userMenuState.x } : undefined}
        slotProps={{ list: { 'aria-label': 'User actions' } }}
      >
        <MenuItem onClick={() => runUserAction('edit-username')}>Edit username</MenuItem>
        {userMenuState?.user?.userType === 'agent' ? <MenuItem onClick={() => runUserAction('api-key')}>Reissue oath token</MenuItem> : null}
        <MenuItem onClick={() => runUserAction('logs')}>See Logs</MenuItem>
        <MenuItem onClick={() => runUserAction('active')}>Active Tickets</MenuItem>
        <MenuItem onClick={() => runUserAction('solved')}>Solved Tickets</MenuItem>
        <MenuItem onClick={() => runUserAction('submitted')}>Submitted</MenuItem>
      </Menu>

      <Menu
        open={Boolean(menuState)}
        onClose={() => setMenuState(null)}
        anchorReference="anchorPosition"
        anchorPosition={menuState ? { top: menuState.y, left: menuState.x } : undefined}
        slotProps={{ list: { 'aria-label': 'Request actions' } }}
      >
        {menuRequest?.purpose === 'credential_recovery' && menuRequest?.requestType === 'human' ? [
          <MenuItem key="password-reset" onClick={() => runAction('password-reset')}>Issue password reset link</MenuItem>
        ] : menuRequest?.purpose === 'credential_recovery' ? [
          <MenuItem key="api-key" onClick={() => { setApiKeyRequest({ ...menuRequest, recoveryId: menuRequest.requestId.replace(/^recovery_/, '') }); setApiKeyActiveDays('30'); setGeneratedApiKey(null); setCopyMessage(''); setMenuState(null); }}>Generate oath token</MenuItem>
        ] : menuRequest?.requestType === 'human' ? [
          menuRequest.status === 'pending' ? <MenuItem key="edit-username" onClick={() => runAction('edit-username')}>Edit username</MenuItem> : null,
          menuRequest.status === 'pending' ? <MenuItem key="setup-link" onClick={() => runAction('setup-link')}>Set password</MenuItem> : null,
          <MenuItem key="remove" onClick={() => runAction('remove')}>Remove request</MenuItem>
        ] : [
          <MenuItem key="api-key" onClick={() => runAction('api-key')}>Generate oath token</MenuItem>,
          <MenuItem key="remove" onClick={() => runAction('remove')}>Remove request</MenuItem>
        ]}
      </Menu>

      <EditUsernameDialog
        open={Boolean(editUsernameRequest)}
        usernameInput={usernameInput}
        saving={saving}
        onUsernameInputChange={setUsernameInput}
        onSave={saveUsername}
        onClose={() => setEditUsernameRequest(null)}
        email={editUsernameRequest?.email || ''}
        userType={editUsernameRequest?.userType || 'human'}
      />

      <ApiKeyDialog
        apiKeyRequest={apiKeyRequest}
        apiKeyActiveDays={apiKeyActiveDays}
        generatedApiKey={generatedApiKey}
        copyMessage={copyMessage}
        saving={saving}
        onActiveDaysChange={setApiKeyActiveDays}
        onGenerate={generateAgentApiKey}
        onCopy={copyGeneratedApiKey}
        onClose={closeApiKeyDialog}
      />
      <PasswordLinkDialog
        generatedSetupLink={generatedSetupLink}
        copyMessage={copyMessage}
        onCopy={copyGeneratedSetupLink}
        onClose={() => { setGeneratedSetupLink(null); setCopyMessage(''); }}
      />
    </section>
  );
}
