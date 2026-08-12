import React, { useEffect, useState } from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControl from '@mui/material/FormControl';
import FormControlLabel from '@mui/material/FormControlLabel';
import FormHelperText from '@mui/material/FormHelperText';
import InputAdornment from '@mui/material/InputAdornment';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select from '@mui/material/Select';
import Step from '@mui/material/Step';
import StepLabel from '@mui/material/StepLabel';
import Stepper from '@mui/material/Stepper';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import Switch from '@mui/material/Switch';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import IconButton from '@mui/material/IconButton';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutlined';
import HighlightOffIcon from '@mui/icons-material/HighlightOff';
import VisibilityIcon from '@mui/icons-material/Visibility';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import { changeFirstRunPassword, completeFirstRun, fetchFirstRunStatus } from '../api/first_run';
import { createProject } from '../api/projects';

const steps = ['Secure root account', 'Create first project', 'Set token limits'];

function humanTtlLabel(minutes) {
  return minutes < 60 ? `${minutes} minutes` : `${minutes / 60} hours`;
}

export default function FirstRunWizard({ token, user, onSessionRevoked }) {
  const [status, setStatus] = useState(null);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [password, setPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [projectName, setProjectName] = useState('');
  const [sensitive, setSensitive] = useState(false);
  const [humanTtl, setHumanTtl] = useState(480);
  const [agentTtl, setAgentTtl] = useState(30);
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmation, setShowConfirmation] = useState(false);

  async function refreshStatus() {
    try {
      const next = await fetchFirstRunStatus(token);
      setStatus(next);
      setHumanTtl(next.humanTokenTtlMinutes || 480);
      setAgentTtl(next.agentOathTtlDays || 30);
    } catch (err) {
      setError(err.message || 'Unable to load first-run setup.');
    }
  }

  useEffect(() => { void refreshStatus(); }, [token]);

  const isRootAdmin = Boolean(status?.isRootAdmin && user.role === 'admin' && user.userType === 'human');
  if (!status || status.phase === 'complete' || !isRootAdmin) return null;

  const activeStep = status.phase === 'password_change_required' ? 0 : status.phase === 'project_required' ? 1 : 2;
  const isPasswordValid = password.length >= 12 && /[0-9]/.test(password) && /[^A-Za-z0-9]/.test(password);
  const isConfirmationValid = isPasswordValid && confirmation.length > 0 && password === confirmation;

  async function submitPassword() {
    setError('');
    if (password !== confirmation) { setError('Password confirmation does not match.'); return; }
    if (!isPasswordValid) {
      setError('Use at least 12 characters, including a number and special character.');
      return;
    }
    setBusy(true);
    try {
      await changeFirstRunPassword(token, password);
      onSessionRevoked();
    } catch (err) {
      setError(err.message || 'Unable to change the root password.');
    } finally { setBusy(false); }
  }

  async function submitProject() {
    setError('');
    if (!projectName.trim()) { setError('Project name is required.'); return; }
    setBusy(true);
    try {
      await createProject(token, projectName.trim(), sensitive ? 'sensitive' : 'normal');
      await refreshStatus();
    } catch (err) {
      setError(err.message || 'Unable to create the first project.');
    } finally { setBusy(false); }
  }

  async function submitTtls() {
    setError('');
    setBusy(true);
    try {
      await completeFirstRun(token, Number(humanTtl), Number(agentTtl));
      sessionStorage.setItem('bug-tracker:first-run-complete', 'Workspace is live. You can now create users, tickets, and AI-agent credentials.');
      window.location.reload();
    } catch (err) {
      setError(err.message || 'Unable to save token limits.');
    } finally { setBusy(false); }
  }

  return (
    <Dialog open fullWidth maxWidth="sm" aria-labelledby="first-run-title">
      <DialogTitle id="first-run-title">Prepare your workspace</DialogTitle>
      <DialogContent dividers>
        <Stepper activeStep={activeStep} alternativeLabel sx={{ mb: 4, '& .MuiStepLabel-label': { typography: 'caption' } }}>
          {steps.map((label) => <Step key={label}><StepLabel>{label}</StepLabel></Step>)}
        </Stepper>
        {error ? <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert> : null}
        {activeStep === 0 ? (
          <Box sx={{ display: 'grid', gap: 2 }}>
            <Typography color="text.secondary">Replace the local bootstrap password before creating projects, users, or agent credentials. You will be signed out immediately afterward.</Typography>
            <TextField
              label="New root password"
              type={showPassword ? 'text' : 'password'}
              autoComplete="new-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              disabled={!isRootAdmin || busy}
              error={!isPasswordValid}
              helperText="At least 12 characters, including a number and special character."
              InputProps={{
                endAdornment: <InputAdornment position="end"><IconButton aria-label={showPassword ? 'Hide root password' : 'Show root password'} onClick={() => setShowPassword((visible) => !visible)} edge="end"><>{showPassword ? <VisibilityOffIcon /> : <VisibilityIcon />}</></IconButton>{isPasswordValid ? <CheckCircleOutlineIcon color="success" aria-label="Password meets security requirements" /> : <HighlightOffIcon color="error" aria-label="Password does not meet security requirements" />}</InputAdornment>
              }}
            />
            <TextField
              label="Confirm root password"
              type={showConfirmation ? 'text' : 'password'}
              autoComplete="new-password"
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
              disabled={!isRootAdmin || busy}
              error={!isConfirmationValid}
              helperText={isConfirmationValid ? 'Passwords match.' : 'Must exactly match the secure root password.'}
              InputProps={{
                endAdornment: <InputAdornment position="end"><IconButton aria-label={showConfirmation ? 'Hide password confirmation' : 'Show password confirmation'} onClick={() => setShowConfirmation((visible) => !visible)} edge="end"><>{showConfirmation ? <VisibilityOffIcon /> : <VisibilityIcon />}</></IconButton>{isConfirmationValid ? <CheckCircleOutlineIcon color="success" aria-label="Password confirmation matches" /> : <HighlightOffIcon color="error" aria-label="Password confirmation does not match" />}</InputAdornment>
              }}
            />
          </Box>
        ) : null}
        {activeStep === 1 ? (
          <Box sx={{ display: 'grid', gap: 2 }}>
            <Typography color="text.secondary">Every ticket belongs to a project. You can add members and create more projects after setup.</Typography>
            <TextField label="First project name" inputProps={{ maxLength: 50 }} value={projectName} onChange={(event) => setProjectName(event.target.value)} disabled={!isRootAdmin || busy} />
            <FormControlLabel control={<Switch checked={sensitive} onChange={(event) => setSensitive(event.target.checked)} disabled={busy} />} label={<Box sx={{ display: 'flex', alignItems: 'center' }}>Sensitive project<Tooltip describeChild title="Sensitive projects require explicit membership for every non-admin user."><IconButton size="small" aria-label="About sensitive projects"><InfoOutlinedIcon fontSize="inherit" /></IconButton></Tooltip></Box>} />
          </Box>
        ) : null}
        {activeStep === 2 ? (
          <Box sx={{ display: 'grid', gap: 2 }}>
            <Typography color="text.secondary">These limits apply to future sign-ins and agent oath-token issuance. Individual agent tokens may be issued for less time.</Typography>
            <FormControl fullWidth disabled={!isRootAdmin || busy}><InputLabel id="human-ttl-label">Human session maximum</InputLabel><Select labelId="human-ttl-label" label="Human session maximum" value={humanTtl} onChange={(event) => setHumanTtl(event.target.value)}>{[15, 30, 60, 240, 480, 720, 1440].map((minutes) => <MenuItem value={minutes} key={minutes}>{humanTtlLabel(minutes)}</MenuItem>)}</Select><FormHelperText>Default: 8 hours. Maximum: 24 hours.</FormHelperText></FormControl>
            <FormControl fullWidth disabled={!isRootAdmin || busy}><InputLabel id="agent-ttl-label">AI oath-token maximum</InputLabel><Select labelId="agent-ttl-label" label="AI oath-token maximum" value={agentTtl} onChange={(event) => setAgentTtl(event.target.value)}>{[1, 7, 14, 30, 45, 62].map((days) => <MenuItem value={days} key={days}>{days} day{days === 1 ? '' : 's'}</MenuItem>)}</Select><FormHelperText>Default: 30 days. Maximum: 62 days.</FormHelperText></FormControl>
          </Box>
        ) : null}
      </DialogContent>
      <DialogActions sx={{ px: 3, py: 2 }}>
        {activeStep === 0 ? <Button variant="contained" onClick={submitPassword} disabled={!isRootAdmin || busy}>Change password and sign in again</Button> : null}
        {activeStep === 1 ? <Button variant="contained" onClick={submitProject} disabled={!isRootAdmin || busy}>Create project</Button> : null}
        {activeStep === 2 ? <Button variant="contained" onClick={submitTtls} disabled={!isRootAdmin || busy}>Activate workspace</Button> : null}
      </DialogActions>
    </Dialog>
  );
}
