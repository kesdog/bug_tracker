import React from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Container from '@mui/material/Container';
import Divider from '@mui/material/Divider';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import ColorModeToggle from './ColorModeToggle';
import { ErrorAlert, LoadingState } from './MuiPrimitives';

export function AuthLayout({ title, description, children, error, successMessage, loading }) {
  return (
    <Box component="main" sx={{ minHeight: '100vh', display: 'grid', placeItems: 'center', p: { xs: 2, sm: 4 }, position: 'relative' }}>
      <Box sx={{ position: 'fixed', top: { xs: 12, sm: 20 }, right: { xs: 12, sm: 20 }, zIndex: (theme) => theme.zIndex.tooltip }}>
        <ColorModeToggle />
      </Box>
      <Container maxWidth="sm" disableGutters>
        <Card
          sx={[
            { width: '100%', borderRadius: 5, backdropFilter: 'blur(24px)' },
            (theme) => theme.applyStyles('dark', {
              borderColor: 'rgba(125, 211, 252, 0.24)',
              boxShadow: '0 30px 90px rgba(0, 0, 0, 0.52)'
            })
          ]}
        >
          <CardContent sx={{ p: { xs: 3, sm: 4 } }}>
            <Stack spacing={2.5}>
              <Box>
                <Typography component="h1" variant="h3">{title}</Typography>
                <Typography color="text.secondary">{description}</Typography>
              </Box>
              {children}
              <ErrorAlert>{error}</ErrorAlert>
              {successMessage ? <Alert severity="success">{successMessage}</Alert> : null}
              {loading ? <LoadingState label="loading" /> : null}
            </Stack>
          </CardContent>
        </Card>
      </Container>
    </Box>
  );
}

export function LoginForm({ email, password, loading, onEmailChange, onPasswordChange, onSubmit, onRequestAccess, onRecoverCredentials }) {
  return (
    <Stack component="form" onSubmit={onSubmit} spacing={2}>
      <TextField id="email" name="email" type="email" autoComplete="email" label="Email" value={email} onChange={(event) => onEmailChange(event.target.value)} placeholder="dev@example.com" fullWidth />
      <TextField id="password" name="password" type="password" autoComplete="current-password" label="Password" value={password} onChange={(event) => onPasswordChange(event.target.value)} placeholder="Enter your password" fullWidth />
      <Button type="submit" disabled={loading} fullWidth>{loading ? 'Signing In...' : 'Sign In'}</Button>
      <Stack direction="row" sx={{ justifyContent: 'space-between' }}>
        <Button type="button" variant="text" onClick={onRequestAccess}>Request Access</Button>
        <Button type="button" variant="text" onClick={onRecoverCredentials}>Forgot Password?</Button>
      </Stack>
    </Stack>
  );
}

export function DemoLoginPanel({ config, onSelect }) {
  if (!config?.accounts?.length) return null;

  return (
    <Stack spacing={2} sx={{ mt: 0.5 }}>
      <Divider>Public demo access</Divider>
      <Alert severity="warning" variant="outlined">
        All data is synthetic, public, and mutable by other visitors. It resets daily at {config.resetAtUtc} UTC.
        Do not enter personal, private, or confidential information.
      </Alert>
      <Box>
        <Typography variant="h6">Choose a role</Typography>
        <Typography variant="body2" color="text.secondary">
          Passwords are visible only because these are intentionally public demo accounts.
        </Typography>
      </Box>
      <Stack spacing={1}>
        {config.accounts.map((account) => (
          <Button
            key={account.role}
            type="button"
            variant="outlined"
            onClick={() => onSelect(account)}
            sx={{ flexDirection: { xs: 'column', sm: 'row' }, alignItems: { xs: 'stretch', sm: 'center' }, justifyContent: 'space-between', gap: 1, px: 2, py: 1.25, borderRadius: 2.5, textAlign: 'left' }}
          >
            <Box component="span" sx={{ minWidth: 0 }}>
              <Typography component="span" sx={{ display: 'block', fontWeight: 800 }}>{account.role}</Typography>
              <Typography component="span" variant="caption" color="text.secondary" sx={{ display: 'block' }}>{account.description}</Typography>
            </Box>
            <Box component="span" sx={{ flexShrink: 0, fontFamily: 'monospace', fontSize: '0.72rem', textAlign: { xs: 'left', sm: 'right' }, overflowWrap: 'anywhere' }}>
              {account.email}<br />{account.password}
            </Box>
          </Button>
        ))}
      </Stack>
      <Box id="demo-account-guidance" sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 2.5 }}>
        <Typography variant="subtitle2">Create your demo access</Typography>
        <Typography variant="body2" color="text.secondary">
          Use existing premade accounts or submit a request to create your own. Give your own agent access to this demo by creating a fictitious AI agent account.
        </Typography>
      </Box>
    </Stack>
  );
}

export function RequestAccessForm({ requestType, email, confirmEmail, loading, isDemo, onTypeChange, onEmailChange, onConfirmEmailChange, onSubmit, onBack }) {
  return (
    <Stack component="form" onSubmit={onSubmit} spacing={2}>
      {isDemo ? (
        <Alert severity="warning" variant="outlined">
          Demo only: use a fictitious email address for identification. No email is sent, and demo data resets daily. Do not use personal or confidential information.
        </Alert>
      ) : null}
      {isDemo ? (
        <Alert severity="info" variant="outlined">
          After submitting, sign in with the Admin demo account and open Users, then Requests, to finish creating the account and issue its password link or AI-agent oath token.
        </Alert>
      ) : null}
      <TextField id="requestType" select label="Type" value={requestType} onChange={(event) => onTypeChange(event.target.value)} fullWidth>
        <MenuItem value="human">Human</MenuItem>
        <MenuItem value="ai_agent">AI agent</MenuItem>
      </TextField>
      <TextField id="requestEmail" type="email" label="Email" value={email} onChange={(event) => onEmailChange(event.target.value)} placeholder="newuser@example.com" fullWidth />
      <TextField id="requestEmailConfirm" type="email" label="Confirm Email" value={confirmEmail} onChange={(event) => onConfirmEmailChange(event.target.value)} placeholder="newuser@example.com" fullWidth />
      <Button type="submit" disabled={loading} fullWidth>{loading ? 'Submitting...' : 'Submit Request'}</Button>
      <Button type="button" variant="text" onClick={onBack}>Back To Sign In</Button>
    </Stack>
  );
}

export function SetupPasswordForm({ email, confirmEmail, password, confirmPassword, loading, onEmailChange, onConfirmEmailChange, onPasswordChange, onConfirmPasswordChange, onSubmit }) {
  return (
    <Stack component="form" onSubmit={onSubmit} spacing={2}>
      <TextField id="setupEmail" type="email" autoComplete="email" label="Email" value={email} helperText="This password link is issued for this email address." slotProps={{ htmlInput: { readOnly: true } }} fullWidth />
      <TextField id="setupEmailConfirm" type="email" autoComplete="email" label="Confirm Email" value={confirmEmail} slotProps={{ htmlInput: { readOnly: true } }} fullWidth />
      <TextField id="newPassword" type="password" label="New Password" value={password} onChange={(event) => onPasswordChange(event.target.value)} placeholder="At least 6 characters" helperText="Use at least 6 characters, including a number and special character." fullWidth />
      <TextField id="newPasswordConfirm" type="password" label="Confirm New Password" value={confirmPassword} onChange={(event) => onConfirmPasswordChange(event.target.value)} placeholder="Repeat new password" fullWidth />
      <Button type="submit" disabled={loading} fullWidth>{loading ? 'Saving...' : 'Set Password'}</Button>
    </Stack>
  );
}

export function CredentialRecoveryForm({ requestType, email, confirmEmail, loading, isDemo, onTypeChange, onEmailChange, onConfirmEmailChange, onSubmit, onBack }) {
  return (
    <Stack component="form" onSubmit={onSubmit} spacing={2}>
      <Alert severity="info" variant="outlined">
        Request a password reset for a human account or an oath-token reissue for an AI agent. For privacy, we do not confirm whether an account exists.
        {isDemo ? ' No email is sent by this demo.' : ''}
      </Alert>
      <TextField id="recoveryType" select label="Account Type" value={requestType} onChange={(event) => onTypeChange(event.target.value)} fullWidth>
        <MenuItem value="human">Human password</MenuItem>
        <MenuItem value="ai_agent">AI agent oath token</MenuItem>
      </TextField>
      <TextField id="recoveryEmail" type="email" label="Email" value={email} onChange={(event) => onEmailChange(event.target.value)} fullWidth />
      <TextField id="recoveryEmailConfirm" type="email" label="Confirm Email" value={confirmEmail} onChange={(event) => onConfirmEmailChange(event.target.value)} fullWidth />
      <Button type="submit" disabled={loading} fullWidth>{loading ? 'Submitting...' : 'Request Recovery'}</Button>
      <Button type="button" variant="text" onClick={onBack}>Back To Sign In</Button>
    </Stack>
  );
}
