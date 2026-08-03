import React from 'react';
import Chip from '@mui/material/Chip';

export const LONG_PRESS_MS = 520;

export function isValidEmail(email) {
  const normalized = email.trim().toLowerCase();
  if (!normalized || normalized.length > 120) {
    return false;
  }

  const atIndex = normalized.indexOf('@');
  if (atIndex <= 0 || atIndex === normalized.length - 1) {
    return false;
  }

  const domain = normalized.slice(atIndex + 1);
  return domain.includes('.') && !domain.startsWith('.') && !domain.endsWith('.');
}

export function openRawTextWindow(title, text) {
  const nextWindow = window.open('', '_blank', 'noopener,noreferrer,width=880,height=560');
  if (!nextWindow) {
    return;
  }

  const escaped = text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');

  nextWindow.document.write(`<!doctype html><html><head><title>${title}</title></head><body style="font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace; padding:16px; background:#0c1329; color:#e6efff;"><h2>${title}</h2><pre style="white-space:pre-wrap; line-height:1.45;">${escaped}</pre></body></html>`);
  nextWindow.document.close();
}

export function RequestStatusChip({ status }) {
  const value = status || 'pending';
  const color = value === 'approved' ? 'success' : value === 'removed' ? 'default' : 'warning';
  return <Chip size="small" label={value} color={color} variant="outlined" sx={{ fontWeight: 800 }} />;
}

function parseUtcTimestamp(value) {
  if (!value) {
    return null;
  }

  const normalized = String(value).includes('T') ? String(value) : `${value.replace(' ', 'T')}Z`;
  const date = new Date(normalized);
  return Number.isNaN(date.getTime()) ? null : date;
}

export function formatLastActive(value) {
  const date = parseUtcTimestamp(value);
  if (!date) {
    return 'Never';
  }

  const elapsedMs = Math.max(0, Date.now() - date.getTime());
  const minutes = Math.max(1, Math.floor(elapsedMs / 60000));
  if (minutes < 60) {
    return `${minutes} min ago`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours} ${hours === 1 ? 'hour' : 'hours'} ago`;
  }

  const days = Math.floor(hours / 24);
  return `${days} ${days === 1 ? 'day' : 'days'} ago`;
}

export function UserPresenceChip({ user }) {
  if (!user?.isActive) {
    return <Chip size="small" color="default" label="Inactive" variant="outlined" sx={{ fontWeight: 800 }} />;
  }

  if (user.userType === 'agent') {
    const connected = user.presenceStatus === 'connected' || user.isOnline;
    return <Chip size="small" color={connected ? 'success' : 'default'} label={connected ? 'Connected' : 'Offline'} variant="outlined" sx={{ fontWeight: 800 }} />;
  }

  if (user.presenceStatus === 'active' || user.isOnline) {
    return <Chip size="small" color="success" label="Active" variant="outlined" sx={{ fontWeight: 800 }} />;
  }

  const lastActive = formatLastActive(user.lastSeenAt);
  return <Chip size="small" color="default" label={lastActive === 'Never' ? 'Offline' : `Last online ${lastActive}`} variant="outlined" sx={{ fontWeight: 800 }} />;
}
