import React from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { formatTicketDate } from '../table_utils';

function buildGeneratedKeyEmailHref(generatedApiKey) {
  if (!generatedApiKey) {
    return '';
  }

  const subject = 'AI oath token for Bug Tracker';
  const body = [
    `Username: ${generatedApiKey.username}`,
    `AI oath token (shown once): ${generatedApiKey.apiKey}`,
    generatedApiKey.expiresAt ? `Expires: ${formatTicketDate(generatedApiKey.expiresAt)}` : '',
    '',
    'Use the username and oath token together when authenticating the AI agent.',
    'Before the agent handles tickets, allocate it to the required projects in Project Management.',
    'After login, connect to GET /api/agent/notifications/ws with the bearer token and reply to ping messages with {"type":"pong"}.'
  ].filter(Boolean).join('\n');

  // Temporary operational delivery only. Replace raw-token email with an authenticated agent handshake protocol.
  return `mailto:${encodeURIComponent(generatedApiKey.email)}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
}

function buildPasswordLinkEmailHref(generatedSetupLink) {
  if (!generatedSetupLink) return '';
  const body = [
    'Use this one-time link to set or reset your Bug Tracker password:',
    generatedSetupLink.link,
    generatedSetupLink.expiresAt ? `Expires: ${formatTicketDate(generatedSetupLink.expiresAt)}` : '',
    '',
    'The link expires after 30 minutes and becomes invalid after use or replacement.'
  ].filter(Boolean).join('\n');
  return `mailto:${encodeURIComponent(generatedSetupLink.email)}?subject=${encodeURIComponent('Bug Tracker password link')}&body=${encodeURIComponent(body)}`;
}

export function EditUsernameDialog({ open, usernameInput, saving, onUsernameInputChange, onSave, onClose, email = '', userType = 'human' }) {
  return (
    <Dialog open={open} onClose={onClose} aria-labelledby="edit-username-title" maxWidth="xs">
      <DialogTitle id="edit-username-title">Edit Username</DialogTitle>
      <DialogContent>
        <TextField
          id="usernameInput"
          label="Username"
          value={usernameInput}
          onChange={(event) => onUsernameInputChange(event.target.value)}
          helperText={email
            ? userType === 'agent'
              ? `Login username for ${email}. The updated username is required at the next agent login.`
              : `Display name for ${email}. Email remains the login.`
            : '3-32 characters: letters, numbers, periods, hyphens, or underscores.'}
          slotProps={{ htmlInput: { minLength: 3, maxLength: 32 } }}
          fullWidth
          autoFocus
          sx={{ mt: 1 }}
        />
      </DialogContent>
      <DialogActions>
        <Button type="button" variant="outlined" onClick={onClose}>Cancel</Button>
        <Button type="button" onClick={onSave} disabled={saving}>{saving ? 'Saving...' : 'Save'}</Button>
      </DialogActions>
    </Dialog>
  );
}

export function ApiKeyDialog({ apiKeyRequest, apiKeyActiveDays, generatedApiKey, copyMessage, saving, onActiveDaysChange, onGenerate, onCopy, onClose }) {
  return (
    <Dialog open={Boolean(apiKeyRequest)} onClose={onClose} aria-labelledby="generate-ai-oath-token-title" maxWidth="sm" fullWidth>
      <DialogTitle id="generate-ai-oath-token-title">Generate AI Oath Token</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Alert severity="warning">
            Generating or reissuing a token immediately disconnects the agent and revokes its existing sessions. This token is shown once; closing this dialog clears it from the screen.
          </Alert>
          <Alert severity="info">
            Before the agent can access tickets, add it to the required projects in Project Management. The agent logs in at <code>/api/auth/agent/login</code> with this username and token, then connects to <code>/api/agent/notifications/ws</code> with its bearer token.
          </Alert>
          <Box>
            <Typography variant="body2" color="text.secondary">Agent username</Typography>
            <Typography sx={{ fontWeight: 900 }}>{apiKeyRequest?.username || apiKeyRequest?.userId || '-'}</Typography>
          </Box>
          <TextField
            id="apiKeyActiveDays"
            label="Active days"
            type="number"
            value={apiKeyActiveDays}
            onChange={(event) => onActiveDaysChange(event.target.value)}
            disabled={Boolean(generatedApiKey)}
            slotProps={{ htmlInput: { min: 1, max: 62 } }}
            helperText="Server-enforced lifespan. Minimum 1 day, maximum 62 days."
            fullWidth
          />
          {generatedApiKey ? <>
            <TextField
              label="AI oath token"
              value={generatedApiKey.apiKey}
              multiline
              minRows={3}
              fullWidth
              slotProps={{ htmlInput: { readOnly: true } }}
            />
            <TextField
              label="Username"
              value={generatedApiKey.username}
              fullWidth
              slotProps={{ htmlInput: { readOnly: true } }}
            />
            {generatedApiKey.expiresAt ? <Typography color="text.secondary">Expires {formatTicketDate(generatedApiKey.expiresAt)}</Typography> : null}
            {copyMessage ? <Alert severity={copyMessage.startsWith('Copied') ? 'success' : 'warning'}>{copyMessage}</Alert> : null}
          </> : null}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button type="button" variant="outlined" onClick={onClose}>Close</Button>
        {generatedApiKey ? <>
          <Button type="button" variant="outlined" onClick={onCopy}>Copy token</Button>
          <Button type="button" component="a" href={buildGeneratedKeyEmailHref(generatedApiKey)}>Email token</Button>
        </> : <Button type="button" onClick={onGenerate} disabled={saving}>{saving ? 'Generating...' : 'Generate token'}</Button>}
      </DialogActions>
    </Dialog>
  );
}

export function PasswordLinkDialog({ generatedSetupLink, copyMessage, onCopy, onClose }) {
  return (
    <Dialog open={Boolean(generatedSetupLink)} onClose={onClose} aria-labelledby="password-link-title" maxWidth="sm" fullWidth>
      <DialogTitle id="password-link-title">Password Link Ready</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <Alert severity="warning">This one-time link expires in 30 minutes and is invalid after use or when a replacement is issued.</Alert>
          <TextField label="Recipient" value={generatedSetupLink?.email || ''} fullWidth slotProps={{ htmlInput: { readOnly: true } }} />
          <TextField label="Set or reset password link" value={generatedSetupLink?.link || ''} multiline minRows={4} fullWidth slotProps={{ htmlInput: { readOnly: true } }} />
          {generatedSetupLink?.expiresAt ? <Typography color="text.secondary">Expires {formatTicketDate(generatedSetupLink.expiresAt)}</Typography> : null}
          {copyMessage ? <Alert severity={copyMessage.startsWith('Copied') ? 'success' : 'warning'}>{copyMessage}</Alert> : null}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button type="button" variant="outlined" onClick={onClose}>Close</Button>
        <Button type="button" variant="outlined" onClick={onCopy}>Copy link</Button>
        <Button type="button" component="a" href={buildPasswordLinkEmailHref(generatedSetupLink)}>Email link</Button>
      </DialogActions>
    </Dialog>
  );
}
