import React from 'react';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import { useColorScheme } from '@mui/material/styles';
import DarkModeIcon from '@mui/icons-material/DarkMode';
import LightModeIcon from '@mui/icons-material/LightMode';

export default function ColorModeToggle({ size = 'medium' }) {
  const { mode, setMode, systemMode } = useColorScheme();
  const activeMode = mode === 'system' ? systemMode : mode;
  const normalizedMode = activeMode || 'dark';
  const nextMode = normalizedMode === 'dark' ? 'light' : 'dark';
  const label = `Switch to ${nextMode} mode`;

  return (
    <Tooltip title={label}>
      <IconButton
        type="button"
        color="inherit"
        size={size}
        aria-label={label}
        onClick={() => setMode(nextMode)}
        sx={{
          border: 1,
          borderColor: 'divider',
          bgcolor: 'background.paper',
          color: 'text.primary',
          boxShadow: '0 10px 30px rgba(15, 23, 42, 0.12)',
          '&:hover': { bgcolor: 'action.hover' }
        }}
      >
        {normalizedMode === 'dark' ? <LightModeIcon fontSize="inherit" /> : <DarkModeIcon fontSize="inherit" />}
      </IconButton>
    </Tooltip>
  );
}
