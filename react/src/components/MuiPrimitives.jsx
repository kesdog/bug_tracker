import React from 'react';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { getTagColor, priorityColors, severityColors, statusColors } from '../theme';

function normalize(value, fallback = '') {
  return String(value || fallback).trim().toLowerCase();
}

export function SeverityChip({ value }) {
  const severity = normalize(value, 'low');
  return <Chip size="small" label={severity} color={severityColors[severity] || 'default'} variant="filled" />;
}

export function PriorityChip({ value }) {
  const priority = normalize(value, 'p2');
  return <Chip size="small" label={priority.toUpperCase()} color={priorityColors[priority] || 'default'} variant="outlined" />;
}

export function StatusChip({ value }) {
  const status = normalize(value, 'todo');
  return <Chip size="small" label={status || '-'} color={statusColors[status] || 'default'} variant="filled" />;
}

export function TagChip({ value }) {
  const tag = String(value || '').trim();
  if (!tag) {
    return <Chip size="small" label="No tags" variant="outlined" />;
  }

  return (
    <Chip
      size="small"
      label={tag}
      variant="outlined"
      sx={{ borderColor: getTagColor(tag), color: getTagColor(tag), bgcolor: `${getTagColor(tag)}18` }}
    />
  );
}

export function PageHeader({ title, description, action, eyebrow }) {
  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} gap={2} sx={{ mb: 2.5, justifyContent: 'space-between', alignItems: { xs: 'flex-start', sm: 'center' } }}>
      <Box>
        {eyebrow ? (
          <Typography variant="overline" color="primary" sx={{ fontWeight: 900, letterSpacing: '0.12em' }}>
            {eyebrow}
          </Typography>
        ) : null}
        <Typography component="h1" variant="h4">
          {title}
        </Typography>
        {description ? <Typography color="text.secondary">{description}</Typography> : null}
      </Box>
      {action || null}
    </Stack>
  );
}

export function MetricCard({ label, value, note, tone = 'primary', onClick, actionLabel }) {
  const content = (
    <>
      <Box aria-hidden="true" sx={{ position: 'absolute', inset: 'auto -28px -42px auto', width: 128, height: 128, borderRadius: '50%', bgcolor: `${tone}.main`, opacity: 0.12 }} />
      <CardContent>
        <Typography variant="overline" color="text.secondary" sx={{ fontWeight: 900 }}>
          {label}
        </Typography>
        <Typography variant="h3" sx={{ fontWeight: 900, lineHeight: 1 }}>
          {value}
        </Typography>
        {note ? <Typography color="text.secondary" sx={{ mt: 1 }}>{note}</Typography> : null}
      </CardContent>
    </>
  );

  return (
    <Card sx={{ height: '100%', position: 'relative', overflow: 'hidden' }}>
      {onClick ? (
        <CardActionArea onClick={onClick} aria-label={actionLabel || label} sx={{ height: '100%', textAlign: 'left' }}>
          {content}
        </CardActionArea>
      ) : content}
    </Card>
  );
}

export function EmptyState({ title, description }) {
  return (
    <Card variant="outlined" sx={{ my: 2 }}>
      <CardContent>
        <Typography variant="h6">{title}</Typography>
        {description ? <Typography color="text.secondary">{description}</Typography> : null}
      </CardContent>
    </Card>
  );
}

export function LoadingState({ label = 'loading' }) {
  return (
    <Stack direction="row" gap={1.5} sx={{ my: 2, alignItems: 'center' }} role="status">
      <CircularProgress size={22} aria-label={label} />
      <Typography color="text.secondary">Loading…</Typography>
    </Stack>
  );
}

export function ErrorAlert({ children }) {
  if (!children) {
    return null;
  }

  return <Alert severity="error" role="alert" sx={{ my: 1.5 }}>{children}</Alert>;
}
